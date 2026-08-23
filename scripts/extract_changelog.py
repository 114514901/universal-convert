import io
import re
import sys


def main():
    version = sys.argv[1]  # 例如 "1.4.0" 或 "1.4.0-dev.1"
    with io.open("CHANGELOG.md", "r", encoding="utf-8") as f:
        text = f.read()

    pattern = re.compile(r"## \[%s\]" % re.escape(version))
    match = pattern.search(text)

    if not match:
        entry = "Release %s" % version
    else:
        start = match.start()
        rest = text[start + 1:]
        next_match = re.search(r"\n## \[", rest)
        end = start + 1 + next_match.start() if next_match else len(text)
        entry = text[start:end].strip()

    with io.open("changelog_entry.md", "w", encoding="utf-8") as f:
        f.write(entry)


if __name__ == "__main__":
    main()
