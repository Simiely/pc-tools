// lib/routes/index.js - 路由注册表：合并各域路由 + 分段匹配器
// 匹配顺序 = 数组顺序；带参路由用「路径分段比较」（零正则，无转义坑）。
// 会话中间件 requireAuth 在 helpers.js（避免本文件与各域路由循环依赖）。
import { userRoutes } from "./users.js";
import { clipRoutes } from "./clips.js";
import { fileRoutes } from "./files.js";
import { syncRoutes } from "./sync.js";

/** 路由预编译：把模式路径切成段，:param 标记为参数位 */
function compile(p) {
  return p.split("/").filter(Boolean).map((s) => (s.startsWith(":") ? { param: s.slice(1) } : { lit: s }));
}

const routes = [...userRoutes, ...clipRoutes, ...fileRoutes, ...syncRoutes].map((r) => ({
  ...r,
  segs: r.p.includes(":") ? compile(r.p) : null,
}));

/** 分段匹配：返回 params 或 null */
function matchSegments(segs, actual) {
  if (segs.length !== actual.length) return null;
  const params = {};
  for (let i = 0; i < segs.length; i++) {
    const s = segs[i];
    if (s.param) {
      try { params[s.param] = decodeURIComponent(actual[i]); } catch { return null; }
    } else if (s.lit !== actual[i]) {
      return null;
    }
  }
  return params;
}

/** 路由匹配：method 相等 + 精确路径或参数路径命中；返回 { handler, params } */
export function matchRoute(url, method) {
  const actual = url.pathname.split("/").filter(Boolean);
  for (const r of routes) {
    if (r.m && r.m !== method) continue;
    if (r.segs) {
      const params = matchSegments(r.segs, actual);
      if (params) return { handler: r.handler, params };
    } else if (r.p === url.pathname) {
      return { handler: r.handler, params: {} };
    }
  }
  return null;
}

/** 把 params 挂到 url 上供 handler 使用（server 层调用） */
export function withParams(url, params) {
  url.params = params;
  return url;
}
