from fastapi import FastAPI
from pydantic import BaseModel
from pathlib import Path
import torch
from transformers import AutoTokenizer, AutoModelForSequenceClassification

MODEL_DIR = Path("/app/model")
ID2LABEL = {0: "impolite", 1: "neutral", 2: "polite"}

app = FastAPI()

tok = AutoTokenizer.from_pretrained(str(MODEL_DIR))
mdl = AutoModelForSequenceClassification.from_pretrained(str(MODEL_DIR))
device = "cuda" if torch.cuda.is_available() else "cpu"
mdl.eval().to(device)

class Req(BaseModel):
    text: str

@app.get("/healthz")
def healthz():
    return {"ok": True}

@app.post("/classify")
def classify(req: Req):
    toks = tok(req.text, return_tensors="pt", truncation=True, max_length=256)
    with torch.no_grad():
        logits = mdl(**{k: v.to(device) for k, v in toks.items()}).logits
    probs = logits.softmax(-1).cpu().numpy()[0]
    idx = int(probs.argmax())
    return {"label": ID2LABEL[idx], "confidence": float(probs.max())}
