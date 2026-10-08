# AGENTS.md · 项目规则

> 📌 **文档基线**：2026-08-13（commit `9f8a90f`）完成四件套
> **更新文档/代码后，请更新此行**（日期 + 新 commit hash），并在 CHANGELOG 追加版本

## 技术栈

- Python 3（标准库，零第三方依赖；Windows 10/11 + Git Bash 环境）
- 前端：单文件原生 HTML/JS/CSS（无框架；ES2017 语法，浏览器直接跑）
- 模板外置：`dirmap_template.html` 由 `scan_map.py` 运行时读取填充

## 关键坑（每条都是踩过的，改动前先看）

- **预览面板 iframe 禁用原生 `prompt/confirm`** → 界面全部用自制模态框（`askInput/askConfirm`），不要用原生弹窗
- **浏览器 localStorage 缓存列宽（colW）优先级高于默认值** → 改列默认宽后用户需点「重置列宽」；代码层有「超宽保护」自动回退
- **列宽总和 > 容器宽 → 必出横向滚动条** → 已做三层 `max-width` 硬限制 + 拖动上限 + 自定义列区补偿（总宽锁定）
- **`thead` 不要用 `position:sticky`** → 工具条换行时表头会错位（曾修过）
- **改模板 JS 后必须跑 `check_js.py` + Edge `--dump-dom` 验证渲染** → 曾因 `const` 重复声明导致整个脚本挂掉、页面空白
- **flex 容器内长文本必须 `min-width:0`** → 否则省略号不生效、撑破布局

## 约定

- UI 标签用中文；代码注释用中文
- 部署形态固定 **3 文件**（html + data.js + serve.py），便携可拷走
- `serve.py` 与 `scan_map.py` 的扫描逻辑为**同源副本**（便携约束），改排除规则/扫描逻辑时**两处需同步**（文件头注释互指）
- 数据模型：`notes[path] = {note, tags[], [列key]: 值}`；兼容旧版纯字符串备注（`normNote` 归一化）

## 常用命令

```bash
python scan_map.py <目标目录> [输出目录]   # 生成地图
python serve.py [端口] [目标目录]          # 启动服务
python check_js.py <生成的html>            # JS 语法自检（必须）
# 渲染验证:
"/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" --headless=new --disable-gpu --dump-dom "file:///D:/.../<目录名>-目录地图.html" | grep c-row
```

## 详细规则（按需 @引用）

- @rules/常见坑.md（如需扩展）
