#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
scan_map.py - 目录地图生成器 (通用版: HTML + data.js, 支持多列编辑/标签/排序)
用法: python scan_map.py [目标目录] [输出目录]
输出:
   <目录名>-目录地图.html     界面
   <目录名>-目录地图.data.js  数据 + 备注/标签/自定义列 (自动写回)
重跑自动合并旧备注/标签/自定义列, 不丢。零第三方依赖。
注意: 扫描逻辑与部署到目标文件夹的 serve.py 为同源副本(便携约束),
修改排除规则或扫描逻辑时两处需同步。
"""
import os
import re
import sys
import json
import time

EXCLUDE_DIRS = {".git", "__pycache__", "node_modules", ".idea", ".vscode",
                "_screenshots", ".svn", "venv", ".venv", "dist", "build"}
EXCLUDE_FILES = {".DS_Store", "Thumbs.db", "desktop.ini", "notes.json",
                 "serve.py", "start_map.bat", "启动目录地图.vbs"}
# 默认自定义列 (可自行增删改)
DEFAULT_COLUMNS = [{"key": "状态"}, {"key": "优先级"}]

TEMPLATE_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                             "dirmap_template.html")


def load_template():
    """读取独立模板文件 (dirmap_template.html, 与 scan_map.py 同目录)"""
    if not os.path.exists(TEMPLATE_FILE):
        sys.stderr.write("缺少模板文件: %s\n" % TEMPLATE_FILE)
        sys.exit(1)
    with open(TEMPLATE_FILE, "r", encoding="utf-8") as f:
        return f.read()


def nat_key(name):
    parts = re.split(r"(\d+)", name.lower())
    return [int(p) if p.isdigit() else p for p in parts]


def build_tree(root, rel=""):
    nodes = []
    try:
        entries = list(os.scandir(os.path.join(root, rel)))
    except OSError:
        return nodes
    items = []
    for e in entries:
        if e.is_dir():
            if e.name in EXCLUDE_DIRS:
                continue
            items.append((e.name, True, None))
        else:
            if e.name in EXCLUDE_FILES:
                continue
            try:
                st = e.stat()
                items.append((e.name, False, st.st_size))
            except OSError:
                items.append((e.name, False, 0))
    items.sort(key=lambda it: (not it[1], nat_key(it[0])))
    for name, is_dir, size in items:
        child_rel = (rel + "/" + name) if rel else name
        if is_dir:
            nodes.append({"n": name, "t": "d", "m": mtime(root, child_rel),
                          "c": build_tree(root, child_rel)})
        else:
            nodes.append({"n": name, "t": "f", "s": size,
                          "m": mtime(root, child_rel)})
    return nodes


def mtime(root, rel):
    try:
        return int(os.path.getmtime(os.path.join(root, rel)))
    except OSError:
        return 0


def count_stats(nodes):
    dirs = files = 0
    total = 0
    max_depth = 0

    def walk(ns, depth):
        nonlocal dirs, files, total, max_depth
        if depth > max_depth:
            max_depth = depth
        for nd in ns:
            if nd["t"] == "d":
                dirs += 1
                walk(nd["c"], depth + 1)
            else:
                files += 1
                total += nd.get("s", 0)
    walk(nodes, 1)
    return dirs, files, total, max_depth


def fmt_bytes(b):
    if b >= 1 << 30:
        return f"{b / (1 << 30):.2f} GB"
    if b >= 1 << 20:
        return f"{b / (1 << 20):.1f} MB"
    if b >= 1 << 10:
        return f"{b / (1 << 10):.0f} KB"
    return f"{b} B"


def read_old_data(data_path):
    """读取旧 data.js 中的备注/标签/列配置, 用于重跑合并"""
    if not os.path.exists(data_path):
        return {}, DEFAULT_COLUMNS
    try:
        with open(data_path, "r", encoding="utf-8") as f:
            content = f.read()
        start = content.find("{")
        end = content.rfind("};")
        if start < 0 or end < 0:
            return {}, DEFAULT_COLUMNS
        obj = json.loads(content[start:end + 1])
        return (obj.get("notes", {}) or {},
                obj.get("columns") or DEFAULT_COLUMNS)
    except Exception:
        return {}, DEFAULT_COLUMNS


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else os.getcwd()
    out_dir = sys.argv[2] if len(sys.argv) > 2 else root
    root = os.path.abspath(root)
    out_dir = os.path.abspath(out_dir)
    if not os.path.isdir(root):
        print("目标目录不存在: " + root)
        sys.exit(1)

    base = os.path.basename(root) or root
    html_name = base + "-目录地图.html"
    data_name = base + "-目录地图.data.js"
    html_path = os.path.join(out_dir, html_name)
    data_path = os.path.join(out_dir, data_name)
    EXCLUDE_FILES.add(html_name)
    EXCLUDE_FILES.add(data_name)

    print("扫描中:", root)
    t0 = time.time()
    tree = build_tree(root)
    dirs, files, total, max_depth = count_stats(tree)
    print(f"扫描完成: {dirs} 目录, {files} 文件, 用时 {time.time()-t0:.1f}s")

    old_notes, columns = read_old_data(data_path)
    data_obj = {
        "data": {
            "root": root,
            "rootName": base,
            "generatedAt": time.strftime("%Y-%m-%d %H:%M"),
            "stats": {"dirs": dirs, "files": files,
                      "totalBytes": total, "maxDepth": max_depth,
                      "totalFmt": fmt_bytes(total)},
            "tree": tree,
        },
        "columns": columns,
        "notes": old_notes,
    }
    payload = json.dumps(data_obj, ensure_ascii=False).replace("</", "<\\/")
    with open(data_path, "w", encoding="utf-8") as f:
        f.write("window.DIRMAP_DATA = " + payload + ";\n")
    print(f"合并保留: {len(old_notes)} 条行数据, 列配置 {[c['key'] for c in columns]}")

    html_doc = (load_template()
                .replace("__TITLE__", base + " 目录地图")
                .replace("__DATA_SRC__", data_name))
    with open(html_path, "w", encoding="utf-8") as f:
        f.write(html_doc)
    print("已生成:")
    print("  " + html_path + f" ({os.path.getsize(html_path)/1024:.0f} KB)")
    print("  " + data_path + f" ({os.path.getsize(data_path)/1024/1024:.1f} MB)")


if __name__ == "__main__":
    main()
