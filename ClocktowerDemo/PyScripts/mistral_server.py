import sys, json, torch
from transformers import AutoTokenizer, AutoModelForCausalLM

MISTRAL_LOCAL_PATH = "mistralai_Mistral-7B-Instruct-v0.3"
DEVICE = "cuda" if torch.cuda.is_available() else "cpu"

tok = AutoTokenizer.from_pretrained(MISTRAL_LOCAL_PATH, local_files_only=True)
model = AutoModelForCausalLM.from_pretrained(MISTRAL_LOCAL_PATH, local_files_only=True, torch_dtype=torch.float16 if DEVICE=="cuda" else torch.float32)
if tok.pad_token is None: tok.pad_token = tok.eos_token
model.to(DEVICE)
model.eval()

def render_prompt(messages):
    # Mistral Instruct format
    buf=[]
    for m in messages:
        if m["role"] == "system":
            buf.append(f"<s>[INST] {m['content']} [/INST]</s>")
        elif m["role"] == "user":
            buf.append(f"<s>[INST] {m['content']} [/INST]")
        elif m["role"] == "assistant":
            buf.append(m["content"])
    return "\n".join(buf)

for line in sys.stdin:
    line=line.strip()
    if not line: continue
    try:
        obj=json.loads(line)
        if obj.get("cmd") == "shutdown":
            print(json.dumps({"ok": True}), flush=True)
            break
        messages = obj.get("messages", [])
        temp = float(obj.get("temperature", 0.6))
        max_new = int(obj.get("max_new_tokens", 256))

        prompt = render_prompt(messages)
        inputs = tok(prompt, return_tensors="pt").to(DEVICE)
        with torch.no_grad():
            out = model.generate(**inputs, max_new_tokens=max_new, do_sample=True, temperature=temp, top_p=0.95)
        text = tok.decode(out[0], skip_special_tokens=True)
        # return only the last assistant part if possible (model formatting can vary)
        content = text.split('[/INST]')[-1].strip()
        print(json.dumps({"content": content}), flush=True)
    except Exception as e:
        print(json.dumps({"error": str(e)}), flush=True)
