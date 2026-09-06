#!/usr/bin/env python3
"""从 steam-description.md 生成 workshop.json 的 title / description。

创意工坊页面的正文是 BBCode，不是 markdown。steam-description.md 里的正文段落已经按
BBCode 写好，这个脚本只负责把它原样搬进 JSON 字符串，避免手工复制时两边走样。

    python publish/build-workshop-json.py

依赖项（创意工坊 item id）和 changeNote 保留原有值，不被覆盖。
"""

import json
import pathlib
import re

HERE = pathlib.Path(__file__).parent
DESC = HERE / "steam-description.md"
OUT = HERE / "workshop.json"

DEFAULTS = {
    "title": "自动观者 | AutoWatcher",
    "visibility": "private",
    "changeNote": "首次发布。",
    "tags": [],
    # 创意工坊 item id，不是 mod id。
    #   3747602295 = RitsuLib
    #   3747526116 = 观者（Boninall）
    # 自动战斗求解器的 item id 还没拿到，发布前必须补上。
    "dependencies": [3747602295, 3747526116],
    "minBranch": None,
    "maxBranch": None,
}


def main() -> None:
    text = DESC.read_text(encoding="utf-8")

    title = re.search(r"## 标题\s*\n+```\n(.+?)\n```", text, re.S)
    if title is None:
        raise SystemExit("steam-description.md 里找不到「## 标题」下的代码块。")

    body = text.split("## 正文", 1)[1]
    body = body.split("\n---\n", 1)[1].strip()

    existing = json.loads(OUT.read_text(encoding="utf-8")) if OUT.exists() else {}
    data = {**DEFAULTS, **existing}
    data["title"] = title.group(1).strip()
    data["description"] = body

    ordered = {
        k: data[k]
        for k in ("title", "description", "visibility", "changeNote", "tags",
                  "dependencies", "minBranch", "maxBranch")
    }
    OUT.write_text(json.dumps(ordered, ensure_ascii=False, indent=2) + "\n",
                   encoding="utf-8", newline="\n")
    print(f"写好 {OUT.name}：标题 {ordered['title']}，正文 {len(body)} 字符")


if __name__ == "__main__":
    main()
