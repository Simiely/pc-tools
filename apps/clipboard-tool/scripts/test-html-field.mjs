// scripts/test-html-field.mjs - 富文本 html 字段后端单测
// 验证: createClip 带 html / publicClip 返回 / 超限截断 / updateClip 清空 / 导出导入携带
process.env.CAP_STORAGE_DIR = "C:/Temp/clipboard-html-test";
import fs from "node:fs";
import { CONFIG } from "../lib/core/config.js";
import { createClip, updateClip, listClips, exportClips, importClips, loadClips } from "../lib/core/clips.js";

let pass = 0, fail = 0;
const ok = (n, c) => { c ? pass++ : fail++; console.log((c ? "✅" : "❌") + " " + n); };

// 清理测试数据
try { fs.rmSync(CONFIG.usersDir, { recursive: true, force: true }); } catch {}
const uid = "11111111-1111-4111-8111-111111111111";

// 1. 普通文本条目(无 html) -> 视图 html 为空
const plain = createClip(uid, { type: "text", title: "纯文本", content: "hello" });
ok("普通条目 html 为空串", plain.html === "");

// 2. 富文本条目(带 html)
const richHtml = "<p><b>你好</b> 世界</p><p>第二段 <a href='https://a.com'>链接</a></p>";
const rich = createClip(uid, { type: "text", title: "富文本", content: "你好 世界\n第二段 链接", html: richHtml });
ok("富文本条目 html 保存", rich.html === richHtml);
ok("富文本条目 content 保留纯文本", rich.content === "你好 世界\n第二段 链接");
ok("列表返回 html 字段", listClips(uid).find(c => c.id === rich.id)?.html === richHtml);

// 3. 超长 html 截断(>512KB)
const big = "x".repeat(CONFIG.MAX_HTML + 100);
const bigClip = createClip(uid, { type: "text", title: "超长", content: "big", html: big });
ok("超长 html 截断到 512KB", bigClip.html.length === CONFIG.MAX_HTML);

// 4. updateClip 清空 html(传空串)
updateClip(uid, rich.id, { html: "" });
ok("updateClip 可清空 html", loadClips(uid).find(c => c.id === rich.id).html === "");

// 5. 重新设置 html
updateClip(uid, rich.id, { html: "<p>更新后</p>" });
ok("updateClip 可更新 html", loadClips(uid).find(c => c.id === rich.id).html === "<p>更新后</p>");

// 6. 导出包含 html
const exp = exportClips(uid);
const expRich = exp.clips.find(c => c.id === rich.id);
ok("导出携带 html", expRich && expRich.html === "<p>更新后</p>");

// 7. 导入携带 html(同 id 取新, 用更新后的)
const imp = importClips(uid, [{ id: rich.id, type: "text", title: "富文本", content: "imp", html: "<p>导入的富文本</p>", updatedAt: Date.now() + 99999 }]);
ok("导入计数 added/updated", imp.ok === true);
ok("导入后 html 被合并", loadClips(uid).find(c => c.id === rich.id)?.html === "<p>导入的富文本</p>");

console.log(`\nhtml 字段单测: ${pass} 通过 / ${fail} 失败`);
process.exit(fail ? 1 : 0);
