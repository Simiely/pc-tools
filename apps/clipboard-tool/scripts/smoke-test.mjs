// scripts/smoke-test.mjs - 剪贴板 API 冒烟测试（本地验证用，非平台测试套件）
// 重要：测试必须指向【独立数据目录的实例】，禁止对平台托管实例(data/tools/clipboard)跑测试，
// 避免测试用户污染/误删真实数据。用法：
//   CAP_STORAGE_DIR=C:/Temp/clip-test node server.mjs 8131   # 独立实例（PowerShell 起，避免 Git Bash 路径转换）
//   node scripts/smoke-test.mjs                               # 默认指向 8131（v0.6.14 起：默认端口 8130→8131，防误连主服务）
//   TEST_PORT=8131 node scripts/smoke-test.mjs                # 显式指定
// ⚠️ v0.6.14 血泪教训：默认端口曾是 8130（用户主服务），测试用户(u815113b 等)污染了真实 .data/users.json。
// 现在默认 8131；如需对自定义端口测试必须显式 TEST_PORT。运行前先确认该端口是独立数据目录实例。
const BASE = "http://127.0.0.1:" + (process.env.TEST_PORT || "8131");
let pass = 0, fail = 0;
const ok = (name, cond) => { cond ? pass++ : fail++; console.log((cond ? "✅" : "❌") + " " + name); };

async function req(method, path, { token, json, form } = {}) {
  const headers = {};
  if (token) headers["Authorization"] = "Bearer " + token;
  let body;
  if (form) { body = form; } // FormData
  else if (json !== undefined) { headers["Content-Type"] = "application/json"; body = JSON.stringify(json); }
  const r = await fetch(BASE + path, { method, headers, body });
  let data = null;
  try { data = await r.json(); } catch {}
  return { status: r.status, data };
}

// 1. 健康
const h = await req("GET", "/health");
ok("健康检查", h.status === 200 && h.data.ok);

// 2. 未登录访问 API → 401
const noAuth = await req("GET", "/api/clips");
ok("无 token 访问返回 401", noAuth.status === 401);

// 3. 新建用户（无密码）
// 幂等性：用户名带随机后缀，重复运行不重名（否则固定名 409 → 后续 data.user 为 undefined 崩）
const RN = Math.floor(Math.random() * 1e6);
const NM1 = "u" + RN + "a", NM2 = "u" + RN + "b";
const u1 = await req("POST", "/api/users", { json: { name: NM1 } });
ok("新建用户(无密码)", u1.status === 201 && u1.data.user.name === NM1 && u1.data.token);
const t1 = u1.data.token;
const uid1 = u1.data.user.id;

// 4. 新建同名用户 → 409
const dup = await req("POST", "/api/users", { json: { name: NM1 } });
ok("重名用户返回 409", dup.status === 409);

// 5. 新建用户（带密码）
const u2 = await req("POST", "/api/users", { json: { name: NM2, password: "secret123" } });
ok("新建用户(带密码)", u2.status === 201);
const uid2 = u2.data.user.id;

// 6. 列表不含哈希，但带 hasPass
const list = await req("GET", "/api/users");
const u2v = list.data.users.find(u => u.id === uid2);
ok("用户列表含 hasPass 且不含 passHash", u2v && u2v.hasPass === true && u2v.passHash === undefined);

// 7. 登录验证：正确密码
const loginOk = await req("POST", "/api/session", { json: { id: uid2, password: "secret123" } });
ok("正确密码登录", loginOk.status === 200 && loginOk.data.token);
// 8. 错误密码 → 401
const loginBad = await req("POST", "/api/session", { json: { id: uid2, password: "wrong" } });
ok("错误密码返回 401", loginBad.status === 401);

// 9. 文本条目 CRUD
const c1 = await req("POST", "/api/clips", { token: t1, json: { type: "text", title: "工作邮箱", content: "hr@example.com", tags: ["工作", "常用"] } });
ok("新增文本条目", c1.status === 201 && c1.data.clip.content === "hr@example.com");
const cid1 = c1.data.clip.id;

// 10. 复制计数
await req("POST", `/api/clips/${cid1}/copy`, { token: t1 });
const c1b = await req("POST", `/api/clips/${cid1}/copy`, { token: t1 });
ok("复制计数累加", c1b.data.copyCount === 2);

// 11. 链接条目
const c2 = await req("POST", "/api/clips", { token: t1, json: { type: "link", url: "https://example.com/docs", title: "文档" } });
ok("新增链接条目", c2.status === 201 && c2.data.clip.url === "https://example.com/docs");
// 非法链接 → 400
const badLink = await req("POST", "/api/clips", { token: t1, json: { type: "link", url: "javascript:alert(1)" } });
ok("非法链接返回 400", badLink.status === 400);

// 12. 搜索
const search = await req("GET", "/api/clips?q=邮箱", { token: t1 });
ok("搜索命中", search.data.clips.length === 1 && search.data.clips[0].id === cid1);
// 标签过滤
const tagF = await req("GET", "/api/clips?tag=工作", { token: t1 });
ok("标签过滤命中", tagF.data.clips.some(c => c.id === cid1));

// 13. 编辑
const edit = await req("PUT", `/api/clips/${cid1}`, { token: t1, json: { title: "HR 邮箱" } });
ok("编辑标题", edit.data.clip.title === "HR 邮箱");

// 14. 数据隔离：NM1 看不到 NM2 的（NM2 还没条目，先建一条）
await req("POST", "/api/clips", { token: u2.data.token, json: { type: "text", content: "小红的秘密" } });
const isolation = await req("GET", "/api/clips", { token: t1 });
ok("用户数据隔离", !isolation.data.clips.some(c => c.content === "小红的秘密"));

// 15. 文件上传 + 下载
const fd = new FormData();
fd.append("file", new Blob(["hello clipboard file"], { type: "text/plain" }), "note.txt");
const up = await req("POST", "/api/files", { token: t1, form: fd });
ok("文件上传", up.status === 201 && up.data.file.fileMime === "text/plain");
const fid = up.data.file.fileId;
const c3 = await req("POST", "/api/clips", { token: t1, json: { type: "file", fileId: fid, fileName: "note.txt", fileSize: up.data.file.fileSize, fileMime: "text/plain" } });
ok("文件条目", c3.status === 201);
const dl = await fetch(`${BASE}/api/files/${fid}`, { headers: { Authorization: "Bearer " + t1 } });
const dlText = await dl.text();
ok("文件下载内容一致", dl.status === 200 && dlText === "hello clipboard file");
// 跨用户下载 → 404（归属校验）
const dl2 = await fetch(`${BASE}/api/files/${fid}`, { headers: { Authorization: "Bearer " + u2.data.token } });
ok("跨用户文件下载被拒", dl2.status === 404);

// 16. 恶意类型拒绝
const badMime = await req("POST", "/api/files", { token: t1, form: fdWithMime("text/html") });
ok("HTML 类型被拒", badMime.status === 415);

// 17. 删除条目联动清理文件
const del = await req("DELETE", `/api/clips/${c3.data.clip.id}`, { token: t1 });
ok("删除条目", del.status === 200);
const dl3 = await fetch(`${BASE}/api/files/${fid}`, { headers: { Authorization: "Bearer " + t1 } });
ok("删除条目后文件已清理", dl3.status === 404);

// 18. 过期条目（1h 不现实等，直接建一个立刻过期的：expire 用 0 分钟不支持，改验证 resolveExpire 合法性——用 '1h' 检查值）
const exp = await req("POST", "/api/clips", { token: t1, json: { type: "text", content: "临时内容", expire: "1h" } });
ok("过期条目带 expireAt", exp.data.clip.expireAt > Date.now());

// 18b. 批量加标签（v0.6.15：对 c1/c2 两条加「批量」标签）
const bAdd = await req("POST", "/api/clips/batch", { token: t1, json: { action: "addTags", ids: [cid1, c2.data.clip.id], tags: ["批量"] } });
ok("批量加标签", bAdd.status === 200 && bAdd.data.affected === 2);
const chkAdd = await req("GET", "/api/clips", { token: t1 });
ok("批量加标签已生效", chkAdd.data.clips.find(c => c.id === cid1).tags.includes("批量") && chkAdd.data.clips.find(c => c.id === c2.data.clip.id).tags.includes("批量"));

// 18c. 批量减标签（v0.6.15：移除「批量」）
const bRm = await req("POST", "/api/clips/batch", { token: t1, json: { action: "removeTags", ids: [cid1, c2.data.clip.id], tags: ["批量"] } });
ok("批量减标签", bRm.status === 200 && bRm.data.affected === 2);
const chkRm = await req("GET", "/api/clips", { token: t1 });
ok("批量减标签已生效", !chkRm.data.clips.find(c => c.id === cid1).tags.includes("批量"));

// 18d. 批量删除（v0.6.15：删 c1/c2 两条）
const bDel = await req("POST", "/api/clips/batch", { token: t1, json: { action: "delete", ids: [cid1, c2.data.clip.id] } });
ok("批量删除", bDel.status === 200 && bDel.data.deleted === 2);
const chkDel = await req("GET", "/api/clips", { token: t1 });
ok("批量删除已生效", !chkDel.data.clips.some(c => c.id === cid1 || c.id === c2.data.clip.id));

// 18e. 批量空选择 → 400（v0.6.15 防御）
const bEmpty = await req("POST", "/api/clips/batch", { token: t1, json: { action: "delete", ids: [] } });
ok("批量空选择返回 400", bEmpty.status === 400);
// 未知 action → 400
const bBad = await req("POST", "/api/clips/batch", { token: t1, json: { action: "nope", ids: [cid1] } });
ok("批量未知操作返回 400", bBad.status === 400);

// 19. 删除他人用户 → 403（须在删自己前：删除会销毁本人全部 token）
const delOther = await req("DELETE", `/api/users/${uid2}`, { token: t1 });
ok("删除他人账号被拒", delOther.status === 403);

// 20. 删除用户
const delUser = await req("DELETE", `/api/users/${uid1}`, { token: t1 });
ok("删除用户", delUser.status === 200);
const afterDel = await req("GET", "/api/users");
ok("用户已从列表移除", !afterDel.data.users.some(u => u.id === uid1));
ok("删除后旧 token 立即失效", (await req("GET", "/api/clips", { token: t1 })).status === 401);

// 21. 回归：删除用户后旧 token 必须失效（走查 P-10：曾可重建幽灵数据）
const u3 = await req("POST", "/api/users", { json: { name: "walkthru" } });
const t3 = u3.data.token, uid3 = u3.data.user.id;
await req("POST", "/api/clips", { token: t3, json: { type: "text", content: "删前数据" } });
await req("DELETE", `/api/users/${uid3}`, { token: t3 });
const ghost = await req("POST", "/api/clips", { token: t3, json: { type: "text", content: "幽灵数据" } });
ok("删用户后旧 token 建条目被拒", ghost.status === 401);
const ghostRead = await req("GET", "/api/clips", { token: t3 });
ok("删用户后旧 token 读列表被拒", ghostRead.status === 401);

// 22. 改密后其他会话失效、当前会话保留（第二轮 R-4）
const u4 = await req("POST", "/api/users", { json: { name: "chgpass", password: "oldpass" } });
const tA = u4.data.token, uid4 = u4.data.user.id;
const loginB = await req("POST", "/api/session", { json: { id: uid4, password: "oldpass" } });
const tB = loginB.data.token;
await req("POST", `/api/users/${uid4}/password`, { token: tA, json: { oldPassword: "oldpass", newPassword: "newpass" } });
ok("改密后当前会话仍有效", (await req("GET", "/api/clips", { token: tA })).status === 200);
ok("改密后其他会话失效", (await req("GET", "/api/clips", { token: tB })).status === 401);
ok("改密后旧密码登录被拒", (await req("POST", "/api/session", { json: { id: uid4, password: "oldpass" } })).status === 401);
ok("新密码登录成功", (await req("POST", "/api/session", { json: { id: uid4, password: "newpass" } })).status === 200);
await req("DELETE", `/api/users/${uid4}`, { token: tA });

console.log(`\n结果: ${pass} 通过 / ${fail} 失败`);
process.exit(fail ? 1 : 0);

function fdWithMime(mime) {
  const f = new FormData();
  f.append("file", new Blob(["<script>x</script>"], { type: mime }), "evil.html");
  return f;
}
