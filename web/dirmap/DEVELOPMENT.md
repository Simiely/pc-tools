# DEVELOPMENT.md · 开发文档

## 项目概览

把任意深层级目录生成一张可视化表格页面（HTML + data.js），支持编辑/标签/排序/列管理，数据自动持久化。面向"内容管理"场景：像电子表格一样管理目录条目，同时每行绑定真实文件路径。

## 架构说明

```
scan_map.py (生成器)                     serve.py (本地服务)
  扫描目录 ──→ dirmap_template.html        静态文件 (html/data.js, no-store 防缓存)
  读模板填充 ──→ HTML + data.js            /open        调起资源管理器打开文件夹
  (重跑合并旧 notes/columns)               /load_notes  读备注(合并进页面)
                                          /save_notes  写回 data.js
                                          /refresh     重扫目录更新 data.js

check_js.py (自检)                        HTML (前端单文件)
  node --check 提取内嵌 JS 语法           分区: 数据/状态/渲染/事件/工具/布局
```

### 数据模型

```js
data.js = {
  data:    { root, rootName, generatedAt, stats, tree },
  columns: [{key:"状态"},{key:"优先级"}, ...],   // 动态列配置
  notes:   { "01source": { note:"", tags:[], "状态":"", ... } }  // 行数据
}
```

- 前端 `loadNotes()` 合并文件数据 + 浏览器本地；`saveNotes()` 是**唯一持久化入口**（写回 data.js + localStorage）
- 双击 HTML 模式：数据存 localStorage；服务模式：自动写回 data.js（随文件走）

### 宽度分配机制（最终规则）

- **左固定区**：操作 90 / 序号 50 / 名称 350 / 标签 150
- **右固定区**：大小 80 / 修改时间 140（最右列无拖宽手柄）
- **中弹性区**：自定义列均分剩余空间；拖动任意列时弹性区补偿，**总宽 = 浏览器宽，无横向滚动**
- 三层硬限制：`html,body{max-width:100%;overflow-x:hidden}` + `main{max-width:100vw}` + `table{max-width:100%}`

## 关键问题与方案

### 问题：预览面板里 prompt/confirm 不弹窗

**TL;DR**：iframe 沙箱静默禁用原生弹窗，标签/加列/改名全部失效。

- 问题：点「+」没反应，原生弹窗不出现
- 根因：预览 iframe 禁用 `prompt/confirm`
- 解决：自制模态框 `askInput()/askConfirm()`（纯 DOM，Enter 确认/Esc 取消/点背景关闭）
- 预防：新增交互一律用自制模态框，不用原生弹窗

### 问题：列默认宽度改了不生效

**TL;DR**：localStorage 缓存列宽（colW）优先级高于模板默认值。

- 问题：模板 SYS_COLS 改 70→90→120 页面都没变化
- 根因：`colWidth(id)` 先查 `colW[id]`（用户拖过/旧版本缓存），再回退默认
- 解决：工具条加「重置列宽」按钮清缓存；新增拖动上限避免拖超
- 预防：改默认宽后提醒用户点重置，或直接清站点 localStorage

### 问题：横向滚动条反复出现

**TL;DR**：列宽总和 > 容器宽必然溢出（依据：table-layout fixed 下列宽总和超父容器即滚动）。

- 问题：拖宽列或历史缓存后出现横向滚动条
- 根因：拖动无上限 + 历史 colW 使固定区总和超屏
- 解决：三层 max-width 硬限制 + 拖动上限（最多吃光弹性区可压缩空间）+ layout 超宽保护（固定区超屏自动回退可调列默认宽）
- 预防：任何宽度改动后跑 Edge dump-dom 核对总宽 = 浏览器宽

### 问题：JS 语法错误导致整页空白

**TL;DR**：`mousemove` 里 `const customs` 重复声明 → SyntaxError → 整个 script 不执行。

- 问题：页面只渲染 header、表格空
- 根因：同一函数作用域内两个 `const customs`（改布局补偿时引入）
- 解决：合并声明；新增 `check_js.py`（node --check 提取内嵌 JS）强制自检
- 预防：**改模板 JS 后必跑 check_js.py + Edge dump-dom 验证渲染，再交付**

### 问题：表头与行错位

**TL;DR**：`thead th{position:sticky;top:56px}` 高度写死，工具条换行时错位。

- 问题：名称列显示错位
- 根因：sticky 固定高度与实际 header 高度不符
- 解决：移除表头 sticky（表头随表格滚动）；flex 长名称补 `min-width:0`
- 预防：新 UI 容器慎用固定高度 sticky
