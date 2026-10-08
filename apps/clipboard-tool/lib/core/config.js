// lib/core/config.js - 剪贴板工具全局配置（单一来源）
// 端口从进程参数/环境变量读取（平台托管时按 manifest.port 传入）；
// 数据目录优先用平台 storage 能力注入的 CAP_STORAGE_DIR，本地开发兜底 .data/。
import path from "node:path";

const PORT = parseInt(process.argv[2], 10) || parseInt(process.env.PORT || "8130", 10);
const storageDir = process.env.CAP_STORAGE_DIR || path.join(process.cwd(), ".data");

export const CONFIG = {
  PORT,
  // 数据目录布局（全部落在平台备份边界内）
  usersFile: path.join(storageDir, "users.json"),          // 用户列表
  usersDir: path.join(storageDir, "users"),                // users/<id>.json 每人一条目文件
  filesDir: path.join(storageDir, "files"),                // files/<uid>/<fileId>.<ext> 文件实体
  // 输入边界（安全）
  MAX_JSON_BODY: 2 * 1024 * 1024,       // JSON 请求体上限 2MB
  MAX_FILE: 10 * 1024 * 1024,           // 单文件上传上限 10MB
  MAX_TITLE: 200,                       // 标题/别名长度
  MAX_CONTENT: 200 * 1024,              // 文本条目内容上限 200KB
  MAX_HTML: 512 * 1024,                 // 富文本 html 内容上限 512KB（标签开销大，放宽 2.5 倍；再大需换存储）
  MAX_TAGS: 10,                         // 单个条目标签数
  MAX_TAG_LEN: 20,
  MAX_USER_NAME: 40,
  ACCOUNT_NAME_RE: /^[A-Za-z0-9]+$/,   // 账号名（身份键）仅限英文+数字——WebDAV 文件名/跨设备识别用，避免路径与编码兼容问题；显示名不受限
  MAX_PASSWORD: 128,
  MAX_USERS: 100,                       // 用户总数上限（防批量建号占磁盘）
  MAX_CLIPS_PER_USER: 500,              // 活跃区条目上限（超出滚动进归档，防 JSON 无限膨胀）
  ARCHIVE_SCAN_LIMIT: 5000,             // 含归档搜索的单次读取上限（防超大归档拖慢）
  // 安全
  SCRYPT_KEYLEN: 64,                    // scrypt 派生密钥长度（字节）
  TOKEN_BYTES: 32,                      // 会话 token 随机长度
  LOGIN_MAX_FAIL: 8,                    // 每 IP 登录失败阈值（限流）
  LOGIN_WINDOW_MS: 60_000,              // 限流窗口 60s
  // 过期清扫
  SWEEP_INTERVAL_MS: 60_000,            // 后台过期清扫周期
  // 排序（单一实现，前端只渲染）
  SORT: { COPY: 1, UPDATED: 2 },        // copyCount 降序 → updatedAt 降序
};

/** 拒绝上传的类型（黑名单：可执行/脚本类，防下载后执行）。
 *  其余类型一律允许——下载强制 attachment + nosniff + 随机文件名，执行风险已由下载侧兜住。
 *  允许空 MIME（浏览器对部分扩展名不给类型，如 .json 在某些环境）。 */
export const BLOCKED_MIME = new Set([
  "text/html", "image/svg+xml",
  "application/x-executable", "application/x-msdownload",
  "application/x-msdos-program", "application/vnd.microsoft.portable-executable",
  "application/x-sh", "application/x-shellscript",
  "application/x-javascript", "text/javascript",
  "application/x-httpd-php",
]);

/** 拒绝的扩展名（与 MIME 双保险；文件名推断用） */
export const BLOCKED_EXT = new Set([
  "html", "htm", "svg", "exe", "bat", "cmd", "sh", "com",
  "js", "mjs", "vbs", "ps1", "php", "jsp", "apk",
]);

/** 常见类型 → 存储扩展名（其余按原始文件名扩展名兜底，随机名防路径穿越） */
export const EXT_BY_MIME = {
  "image/png": "png", "image/jpeg": "jpg", "image/gif": "gif", "image/webp": "webp",
  "application/pdf": "pdf", "application/zip": "zip", "application/json": "json",
  "text/plain": "txt", "text/csv": "csv", "text/markdown": "md",
};

/** 存储扩展名白名单（来自文件名时校验，防路径穿越） */
export const EXT_SAFE_RE = /^[a-z0-9]{1,8}$/;

/** UUID 白名单（所有 id 参数强制校验，防路径穿越） */
export const ID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
