import os
import re

desktop_dir = os.path.join(os.getcwd(), "src", "GDI.Desktop")
for root, _, files in os.walk(desktop_dir):
    for f in files:
        if f.endswith(".xaml"):
            path = os.path.join(root, f)
            with open(path, "r", encoding="utf-8") as handle:
                text = handle.read()
            new_text = re.sub(r'\s*LetterSpacing="[^"]*"', '', text)
            if new_text != text:
                with open(path, "w", encoding="utf-8") as handle:
                    handle.write(new_text)
                print(f"Cleaned {f}")
print("Done")
