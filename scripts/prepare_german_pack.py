"""Convert the German Kokoro pack so KokoroSharp can load it.

KokoroSharp hardcodes ONNX inputs tokens/style/speed (float).
kokoro-onnx exports often use input_ids and int32 speed.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np
import onnx
from onnx import TensorProto, helper


def to_style_npy(arr: np.ndarray) -> np.ndarray:
    a = np.asarray(arr, dtype=np.float32)
    if a.ndim == 3 and a.shape[1] == 1 and a.shape[2] == 256:
        return a
    if a.ndim == 2 and a.shape[-1] == 256:
        # [N, 256] -> [N, 1, 256]
        return a.reshape(a.shape[0], 1, 256)
    if a.ndim == 1 and a.size % 256 == 0:
        n = a.size // 256
        return a.reshape(n, 1, 256)
    raise ValueError(f"Unsupported voice array shape {a.shape}")


def convert_voices(npz_path: Path, out_dir: Path) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    data = np.load(npz_path, allow_pickle=True)
    print("npz keys:", list(data.files))
    for key in data.files:
        arr = to_style_npy(data[key])
        name = key.lower()
        if name in ("martin", "dm_martin"):
            dest = out_dir / "dm_martin.npy"
        elif name in ("victoria", "df_victoria"):
            dest = out_dir / "df_victoria.npy"
        elif name.startswith("d") and "_" in name:
            dest = out_dir / f"{name}.npy"
        else:
            dest = out_dir / "dm_martin.npy"
        np.save(dest, arr)
        print(f"  {key} {arr.shape} -> {dest.name}")


def try_pt_raw(pt_path: Path, dest: Path) -> None:
    """Read a single-tensor PyTorch zip without importing torch."""
    if not pt_path.exists() or dest.exists():
        return
    try:
        from zipfile import ZipFile
    except Exception:
        return
    try:
        with ZipFile(pt_path) as z:
            blobs = [n for n in z.namelist() if n.endswith("/data/0") or n.endswith("data/0")]
            if not blobs:
                print(f"skip {pt_path.name}: no data/0 tensor in zip")
                return
            raw = z.read(blobs[0])
        arr = to_style_npy(np.frombuffer(raw, dtype=np.float32).copy())
        np.save(dest, arr)
        print(f"  {pt_path.name} {arr.shape} -> {dest.name} (raw zip tensor)")
    except Exception as ex:
        print(f"skip {pt_path.name}: {ex}")


def rename_graph_value(graph, old: str, new: str) -> None:
    for value in list(graph.input) + list(graph.output) + list(graph.value_info):
        if value.name == old:
            value.name = new
    for init in graph.initializer:
        if init.name == old:
            init.name = new
    for node in graph.node:
        for i, name in enumerate(node.input):
            if name == old:
                node.input[i] = new
        for i, name in enumerate(node.output):
            if name == old:
                node.output[i] = new


def adapt_onnx(src: Path, dst: Path) -> None:
    print("Loading ONNX:", src)
    model = onnx.load(str(src))
    graph = model.graph
    inputs = {i.name: i for i in graph.input}
    print("inputs:", [(i.name, [d.dim_value or d.dim_param for d in i.type.tensor_type.shape.dim], i.type.tensor_type.elem_type) for i in graph.input])
    print("outputs:", [o.name for o in graph.output])

    if "input_ids" in inputs and "tokens" not in inputs:
        rename_graph_value(graph, "input_ids", "tokens")
        print("renamed input_ids -> tokens")

    speed = next((i for i in graph.input if i.name == "speed"), None)
    if speed is not None and speed.type.tensor_type.elem_type == TensorProto.INT32:
        # KokoroSharp feeds float speed. Keep the int32 consumer as speed_int.
        rename_graph_value(graph, "speed", "speed_int")
        for i, inp in enumerate(list(graph.input)):
            if inp.name == "speed_int":
                del graph.input[i]
                break
        graph.input.append(helper.make_tensor_value_info("speed", TensorProto.FLOAT, [1]))
        graph.node.insert(
            0,
            helper.make_node("Cast", ["speed"], ["speed_int"], to=TensorProto.INT32, name="cast_speed_f32_to_i32"),
        )
        print("wrapped int32 speed with Cast from float")

    if not any(i.name == "tokens" for i in graph.input):
        raise SystemExit("ONNX has no tokens/input_ids input; cannot adapt for KokoroSharp.")
    if not any(i.name == "style" for i in graph.input):
        raise SystemExit("ONNX has no style input; cannot adapt for KokoroSharp.")
    if not any(i.name == "speed" for i in graph.input):
        raise SystemExit("ONNX has no speed input after adapt.")

    mutated = any(n.name == "cast_speed_f32_to_i32" for n in graph.node) or any(
        i.name == "input_ids" for i in graph.input
    )
    if not mutated:
        if src.resolve() != dst.resolve():
            import shutil
            shutil.copy2(src, dst)
            print("Copied unchanged ONNX to", dst)
        else:
            print("ONNX already KokoroSharp-compatible; no write needed")
        return

    onnx.save(model, str(dst))
    print("Wrote", dst, dst.stat().st_size, "bytes")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True)
    args = parser.parse_args()
    root = Path(args.root)
    models = root / "models"
    voices_de = models / "voices-de"
    npz = models / "voices-martin.npz"
    onnx_dst = models / "kokoro-de.onnx"
    onnx_src = models / "kokoro-martin.onnx"
    if not onnx_src.exists():
        onnx_src = onnx_dst

    if not npz.exists():
        raise SystemExit(f"missing {npz}")
    if not onnx_src.exists():
        raise SystemExit(f"missing {onnx_src}")

    convert_voices(npz, voices_de)
    try_pt_raw(models / "victoria.pt", voices_de / "df_victoria.npy")
    adapt_onnx(onnx_src, onnx_dst)
    return 0


if __name__ == "__main__":
    sys.exit(main())
