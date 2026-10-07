#!/usr/bin/env python3
"""Validate a Steam Workshop BBCode description and render a local preview.

    ./devtools/bbcode-preview.py media/steam-description.bbcode

Exits non-zero on a structural error and writes `_bbcode-preview.html` beside
the source, which opens in any browser.

WHY THIS EXISTS: Steam's editor is the only authoritative preview, but it needs
a published item to preview against, and the failure mode is unforgiving — one
unclosed tag and Steam renders the remainder of the page as literal text,
visible to everyone until someone notices. The validator here catches exactly
that class of mistake before upload. The HTML is a proxy for legibility and
structure only; Steam's dialect and CSS are Steam's, so do not treat pixel
differences as bugs.

Pure stdlib, no dependencies.
"""

import base64
import html
import os
import re
import sys

#: Directory the [img] sources resolve against — set in main() to the
#: description's own folder, which is where the media tree lives.
MEDIA_ROOT = "."

#: With --inline, images are embedded as data: URIs so the output is one
#: self-contained file that survives being copied, attached or hosted
#: somewhere with no access to the media tree. Off by default: the plain
#: file is smaller and opens the same in a browser next to media/.
INLINE = False

#: Steam truncates a Workshop item description at this many characters, with
#: no warning, mid-word, on the live page. Counted as PLAIN characters: the
#: v1.2.3 description was cut at file offset 8000 exactly (2026-08-24), so
#: newlines count once, not as CRLF. Change notes are a separate, larger
#: budget and never come close, so this check simply never fires on them.
DESCRIPTION_LIMIT = 8000

#: Tags that carry no closing partner. Steam's list item is the only one in
#: the subset we use — `[*]` runs until the next item or the end of the list.
UNPAIRED = {"*"}

#: Paired tags this renderer knows. Anything else is reported rather than
#: silently passed through, because an unrecognised tag on the page is far
#: more likely to be our typo than a Steam feature we forgot.
PAIRED = {
    "h1", "h2", "h3", "b", "i", "u", "strike",
    "list", "olist", "url", "img", "quote", "code", "spoiler", "noparse", "hr",
}

#: Tags the renderer still understands but Steam will not accept, mapped to the
#: reason. Kept renderable so old files still preview, reported as an error so a
#: new one cannot ship with them. Steam's tag support is not a fixed contract:
#: every entry here published successfully at some point and stopped.
REJECTED = {
    "hr": ("Steam's description editor refuses to SAVE a page containing it, "
           "with only a generic 'there was a problem trying to save' error and "
           "no indication which tag is at fault (observed 2026-08-24, item "
           "3783456242, persistent over hours). The same tag published fine at "
           "v1.1.0 through v1.2.2, so this changed on Steam's side"),
}

TAG = re.compile(r"\[(/?)([a-zA-Z*]+)(?:=([^\]]*))?\]")


def validate(text):
    """Return a list of human-readable problems, empty when the file is sound."""
    problems = []
    stack = []
    for match in TAG.finditer(text):
        closing, name, _ = match.groups()
        name = name.lower()
        line = text.count("\n", 0, match.start()) + 1

        if name in REJECTED and not closing:
            problems.append(f"line {line}: [{name}] — {REJECTED[name]}")
            # Fall through: still balance-check it, so removing the opener
            # later does not produce a phantom "closes nothing".
        if name in UNPAIRED:
            continue
        if name not in PAIRED:
            problems.append(f"line {line}: unknown tag [{name}]")
            continue

        if not closing:
            stack.append((name, line))
        elif not stack:
            problems.append(f"line {line}: [/{name}] closes nothing")
        elif stack[-1][0] != name:
            open_name, open_line = stack[-1]
            problems.append(
                f"line {line}: [/{name}] closes [{open_name}] opened on line {open_line}"
            )
            stack.pop()
        else:
            stack.pop()

    for name, line in stack:
        problems.append(f"line {line}: [{name}] is never closed")
    return problems


def render(text):
    """BBCode subset to HTML. Structure only — this is not a Steam emulator."""
    out = html.escape(text)

    # Lists first: the items have to become <li> before the surrounding
    # paragraph pass turns their blank lines into <p>.
    def as_list(match):
        tag = "ol" if match.group(1) == "olist" else "ul"
        items = [i.strip() for i in match.group(2).split("[*]") if i.strip()]
        return f"<{tag}>" + "".join(f"<li>{i}</li>" for i in items) + f"</{tag}>"

    out = re.sub(r"\[(list|olist)\](.*?)\[/\1\]", as_list, out, flags=re.S)

    out = re.sub(r"\[h1\](.*?)\[/h1\]", r"<h1>\1</h1>", out, flags=re.S)
    out = re.sub(r"\[h2\](.*?)\[/h2\]", r"<h2>\1</h2>", out, flags=re.S)
    out = re.sub(r"\[h3\](.*?)\[/h3\]", r"<h3>\1</h3>", out, flags=re.S)
    out = re.sub(r"\[b\](.*?)\[/b\]", r"<strong>\1</strong>", out, flags=re.S)
    out = re.sub(r"\[i\](.*?)\[/i\]", r"<em>\1</em>", out, flags=re.S)
    out = re.sub(r"\[u\](.*?)\[/u\]", r"<u>\1</u>", out, flags=re.S)
    out = re.sub(r"\[strike\](.*?)\[/strike\]", r"<s>\1</s>", out, flags=re.S)
    out = re.sub(r"\[quote\](.*?)\[/quote\]", r"<blockquote>\1</blockquote>", out, flags=re.S)
    out = re.sub(r"\[code\](.*?)\[/code\]", r"<pre>\1</pre>", out, flags=re.S)
    out = re.sub(r"\[hr\]\[/hr\]", "<hr>", out)
    out = re.sub(r"\[url=([^\]]+)\](.*?)\[/url\]", r'<a href="\1">\2</a>', out, flags=re.S)
    out = re.sub(r"\[url\](.*?)\[/url\]", r'<a href="\1">\1</a>', out, flags=re.S)

    # An unresolved image is drawn as a labelled box rather than a broken
    # icon: the placeholders are the point of the check at this stage.
    def as_img(match):
        src = match.group(1).strip()
        if src.startswith("http"):
            return f'<img src="{src}" alt="">'
        # Any local file that actually exists renders. This used to match
        # `cards/` only, which quietly reduced every gif on the page to a
        # placeholder box — so the preview certified the page's structure
        # while showing none of its footage, which is most of the page.
        local = os.path.join(MEDIA_ROOT, src)
        if os.path.isfile(local):
            if INLINE:
                mime = "image/gif" if src.endswith(".gif") else "image/png"
                with open(local, "rb") as handle:
                    data = base64.b64encode(handle.read()).decode("ascii")
                return f'<img src="data:{mime};base64,{data}" alt="">'
            return f'<img src="{src}" alt="">'
        return f'<div class="ph">[img] {src} (missing)</div>'

    out = re.sub(r"\[img\](.*?)\[/img\]", as_img, out, flags=re.S)

    # Steam renders EVERY newline as a line break, so the preview must too —
    # the earlier version reflowed wrapped lines, certifying a source that
    # Steam would render with a break mid-sentence at every 76th column.
    blocks = [b.strip().replace("\n", "<br>") for b in re.split(r"\n\s*\n", out) if b.strip()]
    return "\n".join(
        b if b.startswith(("<h", "<ul", "<ol", "<bl", "<pre", "<hr", "<div"))
        else f"<p>{b}</p>"
        for b in blocks
    )


PAGE = """<!doctype html><meta charset="utf-8"><title>{title}</title>
<style>
 body {{ background:#1b2838; color:#c6d4df; font:16px/1.5 Arial,sans-serif;
        margin:0; padding:40px; }}
 .wrap {{ max-width:780px; margin:0 auto; background:#16202d; padding:32px 36px;
        border-radius:4px; }}
 h1 {{ color:#fff; font-size:28px; margin:.2em 0 .6em; }}
 h2 {{ color:#fff; font-size:20px; margin:1.6em 0 .5em;
       border-bottom:1px solid #2a3f5a; padding-bottom:6px; }}
 h3 {{ color:#dfe3e6; font-size:17px; }}
 a {{ color:#66c0f4; }}
 strong {{ color:#fff; }}
 li {{ margin:.3em 0; }}
 hr {{ border:0; border-top:1px solid #2a3f5a; margin:1.6em 0; }}
 img {{ max-width:100%; }}
 blockquote {{ border-left:3px solid #2a3f5a; margin:1em 0; padding:.4em 1em;
               color:#8f98a0; }}
 .ph {{ background:#0e1620; border:1px dashed #3d5875; color:#7a8b9c;
        padding:22px; text-align:center; font-family:monospace; margin:1em 0; }}
 .note {{ max-width:780px; margin:0 auto 18px; color:#7a8b9c; font-size:13px; }}
</style>
<div class="note">Local approximation — Steam's own editor is authoritative.</div>
<div class="wrap">{body}</div>
"""


def main():
    global MEDIA_ROOT, INLINE

    args = [a for a in sys.argv[1:] if a != "--inline"]
    INLINE = "--inline" in sys.argv[1:]
    if len(args) != 1:
        print(__doc__.strip().splitlines()[2].strip(), file=sys.stderr)
        return 2

    source = args[0]
    MEDIA_ROOT = os.path.dirname(os.path.abspath(source))
    with open(source, encoding="utf-8") as handle:
        text = handle.read()

    problems = validate(text)
    for problem in problems:
        print(f"error: {problem}", file=sys.stderr)

    dest = os.path.join(MEDIA_ROOT,
                        "_bbcode-preview-inline.html" if INLINE
                        else "_bbcode-preview.html")
    # The description's image URLs point at the public repo, which does not
    # exist until first publish — substitute the local files those URLs will
    # eventually serve, for RENDERING ONLY. Validation and the character
    # count above run on the real text, because that is what ships.
    preview_text = text.replace(
        "https://raw.githubusercontent.com/beverage/neat-edges/main/media/",
        "")
    with open(dest, "w", encoding="utf-8") as handle:
        handle.write(PAGE.format(title=os.path.basename(source),
                                 body=render(preview_text)))

    placeholders = len(re.findall(r"REPLACE-WITH-[A-Z-]+", text))
    over_limit = len(text) > DESCRIPTION_LIMIT
    print(f"wrote {dest}")
    print(f"  {len(TAG.findall(text))} tags, {len(problems)} problems, "
          f"{placeholders} placeholders left")
    print(f"  {len(text)} chars, "
          f"{abs(DESCRIPTION_LIMIT - len(text))} "
          f"{'OVER' if over_limit else 'under'} the {DESCRIPTION_LIMIT} limit")
    if over_limit:
        print(f"error: Steam truncates the description at {DESCRIPTION_LIMIT} "
              f"characters, silently and mid-word. Cut "
              f"{len(text) - DESCRIPTION_LIMIT} characters.", file=sys.stderr)
    return 1 if (problems or over_limit) else 0


if __name__ == "__main__":
    sys.exit(main())
