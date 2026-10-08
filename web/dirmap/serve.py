#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
serve.py - 目录地图本地服务 (通用版)
放在目标文件夹内使用, 提供:
  1. 备注自动写回 <目录名>-目录地图.data.js (随文件走)
  2. 「刷新目录」: 页面点击刷新按钮 -> 重新扫描目录 -> 更新 data.js, 备注自动合并
  3. 「打开」文件夹直达系统资源管理器/访达
用法: python serve.py [端口]   (默认 8765, 占用自动顺延)
跨平台: Windows / macOS。零第三方依赖。
注意: 为保持「3 文件可拷走」的便携约束, 扫描逻辑(build_tree 等)与
D:\\work\\scan_map.py 中为同源副本, 修改排除规则或扫描逻辑时两处需同步。
"""
import os
import re
import sys
import json
import time
import subprocess
import webbrowser
import threading
from http.server import HTTPServer, SimpleHTTPRequestHandler
from urllib.parse import urlparse, unquote, quote

ROOT = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.basename(ROOT) or ROOT
HTML_NAME = BASE + "-目录地图.html"
DATA_NAME = BASE + "-目录地图.data.js"
DATA_PATH = os.path.join(ROOT, DATA_NAME)
# 目标目录: 优先用命令行第 2 个参数指定; 否则扫描 serve.py 所在目录
TARGET = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else ROOT
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8765


def resolve_names():
    """自动识别同目录中的地图文件, 目录改名/换位置也能用"""
    global HTML_NAME, DATA_NAME, DATA_PATH
    try:
        for f in os.listdir(ROOT):
            if f.endswith("-目录地图.html") and os.path.isfile(os.path.join(ROOT, f)):
                HTML_NAME = f
                break
    except OSError:
        pass
    DATA_NAME = HTML_NAME[:-5] + ".data.js"
    DATA_PATH = os.path.join(ROOT, DATA_NAME)

EXCLUDE_DIRS = {".git", "__pycache__", "node_modules", ".idea", ".vscode",
                "_screenshots", ".svn", "venv", ".venv", "dist", "build"}
EXCLUDE_FILES = {".DS_Store", "Thumbs.db", "desktop.ini", "notes.json",
                 "serve.py", "start_map.bat", "启动目录地图.vbs",
                 HTML_NAME, DATA_NAME}


# ---------- 目录扫描 (与 scan_map.py 一致) ----------
def nat_key(name):
    parts = re.split(r"(\d+)", name.lower())
    return [int(p) if p.isdigit() else p for p in parts]


def _mtime(root, rel):
    try:
        return int(os.path.getmtime(os.path.join(root, rel)))
    except OSError:
        return 0


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
            nodes.append({"n": name, "t": "d", "m": _mtime(root, child_rel),
                          "c": build_tree(root, child_rel)})
        else:
            nodes.append({"n": name, "t": "f", "s": size,
                          "m": _mtime(root, child_rel)})
    return nodes


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


def read_data_obj():
    """读 data.js, 返回 (ok, obj)"""
    if not os.path.exists(DATA_PATH):
        return False, {}
    try:
        with open(DATA_PATH, "r", encoding="utf-8") as f:
            content = f.read()
        start = content.find("{")
        end = content.rfind("};")
        if start < 0 or end < 0:
            return False, {}
        return True, json.loads(content[start:end + 1])
    except Exception:
        return False, {}


def write_data_obj(obj):
    payload = json.dumps(obj, ensure_ascii=False).replace("</", "<\\/")
    with open(DATA_PATH, "w", encoding="utf-8") as f:
        f.write("window.DIRMAP_DATA = " + payload + ";\n")


def refresh_scan():
    """重新扫描目标目录并更新 data.js, 保留旧备注; 返回 (ok, msg)"""
    try:
        target = TARGET
        if not os.path.isdir(target):
            return False, "目标目录不存在: " + target
        tree = build_tree(target)
        dirs, files, total, max_depth = count_stats(tree)
        ok, old = read_data_obj()
        notes = old.get("notes", {}) if ok else {}
        columns = old.get("columns") if ok else None
        obj = {
            "data": {
                "root": target,
                "rootName": os.path.basename(target) or target,
                "generatedAt": time.strftime("%Y-%m-%d %H:%M"),
                "stats": {"dirs": dirs, "files": files,
                          "totalBytes": total, "maxDepth": max_depth,
                          "totalFmt": fmt_bytes(total)},
                "tree": tree,
            },
            "notes": notes,
        }
        if columns:
            obj["columns"] = columns
        write_data_obj(obj)
        return True, "%d 目录 / %d 文件, 备注 %d 条已保留" % (dirs, files, len(notes))
    except Exception as e:
        return False, str(e)


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *a, **kw):
        super().__init__(*a, directory=ROOT, **kw)

    def end_headers(self):
        # data.js 与 HTML 禁止缓存, 保证「刷新目录」后浏览器立即拿到新数据
        path = urlparse(self.path).path
        if path.endswith(".js") or path.endswith(".html"):
            self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def _send_json(self, obj, code=200):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _safe_path(self, rel):
        full = os.path.normpath(os.path.join(ROOT, rel.replace("/", os.sep)))
        if full != ROOT and not full.startswith(ROOT + os.sep):
            return None
        return full

    def do_GET(self):
        parsed = urlparse(self.path)
        if parsed.path == "/open":
            rel = unquote(parsed.query[5:]) if parsed.query.startswith("path=") else ""
            full = self._safe_path(rel) if rel else None
            if not full or not os.path.exists(full):
                self._send_json({"ok": False, "msg": "路径无效: " + rel}, 400)
                return
            try:
                if sys.platform == "win32":
                    full = os.path.normpath(full)
                    if os.path.isdir(full):
                        subprocess.Popen('explorer "%s"' % full)
                    else:
                        subprocess.Popen('explorer /select,"%s"' % full)
                else:
                    subprocess.Popen(["open", full])
                self._send_json({"ok": True})
            except Exception as e:
                self._send_json({"ok": False, "msg": "打开失败: " + str(e)}, 500)
            return
        if parsed.path == "/load_notes":
            ok, obj = read_data_obj()
            self._send_json({"notes": obj.get("notes", {}) if ok else {}})
            return
        super().do_GET()

    def do_POST(self):
        parsed = urlparse(self.path)
        if parsed.path == "/save_notes":
            try:
                length = int(self.headers.get("Content-Length", 0))
                payload = json.loads(self.rfile.read(length).decode("utf-8"))
                ok, obj = read_data_obj()
                if not ok:
                    self._send_json({"ok": False, "msg": "未找到数据文件"}, 500)
                    return
                obj["notes"] = payload.get("notes", {})
                if "columns" in payload and payload["columns"]:
                    obj["columns"] = payload["columns"]
                write_data_obj(obj)
                self._send_json({"ok": True})
            except Exception as e:
                self._send_json({"ok": False, "msg": str(e)}, 500)
            return
        if parsed.path == "/refresh":
            ok, msg = refresh_scan()
            self._send_json({"ok": ok, "msg": msg} if ok else {"ok": False, "msg": msg},
                            200 if ok else 500)
            return
        self.send_error(404)

    def log_message(self, fmt, *args):
        sys.stdout.write("[%s] %s\n" % (self.log_date_time_string(), fmt % args))


def main():
    resolve_names()
    print("目录地图本地服务 (备注写回 data.js, 支持刷新)", flush=True)
    print("  程序目录: %s" % ROOT, flush=True)
    print("  扫描目录: %s" % TARGET, flush=True)
    print("  地图:   %s" % HTML_NAME, flush=True)

    server = None
    port = PORT
    for p in range(PORT, PORT + 10):
        try:
            server = HTTPServer(("127.0.0.1", p), Handler)
            port = p
            break
        except OSError:
            continue
    if server is None:
        print("  端口 %d~%d 均被占用, 请指定其他端口: python serve.py 9000" %
              (PORT, PORT + 9), flush=True)
        sys.exit(1)

    url = "http://127.0.0.1:%d/%s" % (port, quote(HTML_NAME))
    print("  地址:   %s" % url, flush=True)
    print("  如果浏览器没有自动打开, 请手动复制上面地址到浏览器访问", flush=True)
    print("  按 Ctrl+C 停止", flush=True)

    def _open():
        try:
            webbrowser.open(url)
        except Exception:
            pass
    threading.Timer(0.8, _open).start()

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n已停止", flush=True)


if __name__ == "__main__":
    main()
