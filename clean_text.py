
# -*- coding: utf-8 -*-

import sys
import unicodedata

def sanitize(text: str) -> str:
    cleaned_chars = []
    for ch in text:
        # Giữ lại newline và tab
        if ch in ['\n', '\t']:
            cleaned_chars.append(ch)
            continue
        cat = unicodedata.category(ch)
        # Loại bỏ control (Cc) và format (Cf)
        if cat in ('Cc', 'Cf'):
            continue
        cleaned_chars.append(ch)
    return ''.join(cleaned_chars)

def main():
    if sys.stdin.isatty():
        print("Cách dùng: python clean_text.py < input.txt > output.txt")
        return
    data = sys.stdin.read()
    cleaned = sanitize(data)
    sys.stdout.write(cleaned)

if __name__ == "__main__":
    main()
