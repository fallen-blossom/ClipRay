import argparse, base64, contextlib, json, os, sys
from pathlib import Path

import cv2, numpy as np
from paddleocr import TextDetection, TextRecognition


def create_pipeline(models: Path) -> tuple[TextDetection, TextRecognition]:
    # 常驻进程只初始化一次，避免每张截图重复 import 和加载 140 MB 级模型。
    with contextlib.redirect_stdout(sys.stderr):
        common = {
            "device": "cpu",
            # 当前 Windows Paddle 3.3.1 + PP-OCRv6 medium 会在 oneDNN 路径抛出
            # ConvertPirAttribute2RuntimeAttribute；保持纯 Paddle CPU 才能真正使用 PP-OCR，避免静默退回 Windows OCR。
            "enable_mkldnn": False,
            "cpu_threads": max(1, min(8, os.cpu_count() or 1)),
        }
        return (
            TextDetection(
                model_dir=str(models / "PP-OCRv6_medium_det_infer"),
                **common,
            ),
            TextRecognition(
                model_dir=str(models / "PP-OCRv6_medium_rec_infer"),
                **common,
            ),
        )


def crop_polygon(image: np.ndarray, polygon: list[list[int]]) -> np.ndarray:
    points = np.asarray(polygon, dtype=np.float32)
    width = max(1, int(round(max(np.linalg.norm(points[0] - points[1]), np.linalg.norm(points[2] - points[3])))))
    height = max(1, int(round(max(np.linalg.norm(points[0] - points[3]), np.linalg.norm(points[1] - points[2])))))
    destination = np.asarray([[0, 0], [width - 1, 0], [width - 1, height - 1], [0, height - 1]], dtype=np.float32)
    matrix = cv2.getPerspectiveTransform(points, destination)
    return cv2.warpPerspective(image, matrix, (width, height), borderMode=cv2.BORDER_REPLICATE)


def recognize(pipeline: tuple[TextDetection, TextRecognition], encoded: str) -> dict:
    image = cv2.imdecode(np.frombuffer(base64.b64decode(encoded), np.uint8), cv2.IMREAD_COLOR)
    detector, recognizer = pipeline
    detected = detector.predict(image)[0].json["res"]
    polygons = detected["dt_polys"]
    if not polygons:
        return {"words": []}
    crops = [crop_polygon(image, polygon) for polygon in polygons]
    recognized = recognizer.predict(crops)
    words = []
    for polygon, result in zip(polygons, recognized):
        data = result.json["res"]
        points = np.asarray(polygon)
        x1, y1 = points.min(axis=0)
        x2, y2 = points.max(axis=0)
        words.append({"text": data["rec_text"], "x": int(x1), "y": int(y1), "width": int(x2 - x1), "height": int(y2 - y1)})
    return {"words": words}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--models", required=True)
    args = parser.parse_args()
    ocr = create_pipeline(Path(args.models))
    for line in sys.stdin:
        if not line.strip():
            continue
        try:
            payload = json.loads(line)
            if payload.get("warmup"):
                print(json.dumps({"ready": True}), flush=True)
                continue
            with contextlib.redirect_stdout(sys.stderr):
                result = recognize(ocr, payload["image"])
            print(json.dumps(result, ensure_ascii=False), flush=True)
        except Exception:
            print(json.dumps({"error": "ocr failed"}), flush=True)


if __name__ == "__main__":
    main()
