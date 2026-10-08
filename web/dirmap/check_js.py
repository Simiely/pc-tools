#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
check_js.py - 检查 目录地图.html 内嵌 JS 语法 (需要 node 在 PATH)
用法: python check_js.py <html 文件>
每次重新生成 HTML 后运行一次, 防止 JS 语法错误(如重复声明)进入交付物。
"""
import re
import sys
import os
import subprocess
import tempfile


def main():
    if len(sys.argv) < 2:
        print("用法: python check_js.py <html文件>")
        sys.exit(2)
    html = sys.argv[1]
    src = open(html, encoding="utf-8").read()
    scripts = re.findall(r"<script>(.*?)</script>", src, re.S)
    if not scripts:
        print("无内嵌脚本")
        sys.exit(0)
    tmp = os.path.join(tempfile.gettempdir(), "_dirmap_check.js")
    with open(tmp, "w", encoding="utf-8") as f:
        f.write(scripts[0])
    try:
        r = subprocess.run(["node", "--check", tmp],
                           capture_output=True, text=True, timeout=30)
    except FileNotFoundError:
        print("未找到 node, 跳过检查")
        sys.exit(0)
    if r.returncode != 0:
        print("JS 语法错误:")
        print(r.stderr)
        sys.exit(1)
    print("JS 语法 OK (%d 字符)" % len(scripts[0]))


if __name__ == "__main__":
    main()
