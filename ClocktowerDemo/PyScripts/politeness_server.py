import sys, json, torch
from transformers import AutoTokenizer, AutoModelForSequenceClassification

DEBERTA_MODEL_DIR = "politeness_detector_e99_lr1e-08"
ID2LABEL = {0: "impolite", 1: "neutral", 2: "polite"}

tokenizer = AutoTokenizer.from_pretrained(DEBERTA_MODEL_DIR)
model = AutoModelForSequenceClassification.from_pretrained(DEBERTA_MODEL_DIR)
device = "cuda" if torch.cuda.is_available() else "cpu"
model.eval().to(device)

def classify(text: str):
    toks = tokenizer(text, return_tensors="pt", truncation=True, max_length=256).to(device)
    with torch.no_grad():
        logits = model(**toks).logits
        probs = logits.softmax(-1).cpu().numpy()[0]
    label_idx = int(probs.argmax())
    return {"label": ID2LABEL[label_idx], "confidence": float(probs.max())}

for line in sys.stdin:
    line=line.strip()
    if not line: continue
    try:
        obj = json.loads(line)
        if obj.get("cmd") == "shutdown":
            print(json.dumps({"ok": True}), flush=True)
            break
        text = obj.get("text","")
        res = classify(text)
        print(json.dumps(res), flush=True)
    except Exception as e:
        print(json.dumps({"error": str(e)}), flush=True)
