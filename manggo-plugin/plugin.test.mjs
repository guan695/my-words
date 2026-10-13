import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
const source = await readFile(new URL("./main.js", import.meta.url), "utf8");
const { add } = await import("data:text/javascript;base64," + Buffer.from(source).toString("base64"));
let calls = [];
const input = { sourceText: "Approach", translatedText: "方法", translationResult: { dictionary: { pronunciations: [{ phonetic: "/əˈproʊtʃ/" }], meanings: [{ partOfSpeech: "n." }] } } };
const options = { config: { token: "test-only" }, utils: { fetch: async (url, init) => {
  calls.push({ url, ...init });
  return { ok: true, json: async () => ({ success: true, message: "已收藏 5 次，重复 4 次" }) };
} } };
assert.equal((await add(input, options)).message, "已收藏 5 次，重复 4 次");
assert.equal(calls[0].url, "http://127.0.0.1:17653/words");
assert.equal(JSON.parse(calls[0].body).phonetic, "/əˈproʊtʃ/");
assert.equal(JSON.parse(calls[0].body).word, "Approach");
await assert.rejects(() => add(input, { ...options, config: {} }), /连接口令/);
await assert.rejects(() => add({}, options), /单词或短语/);
const requests = [];
await add(input, { config: options.config, utils: { fetch: async (_, init) => {
  requests.push(init.headers["X-Request-Id"]);
  if (requests.length === 1) throw new Error("response lost");
  return { ok: true, json: async () => ({ success: true, message: "已收录" }) };
} } });
assert.equal(requests.length, 2);
assert.equal(requests[0], requests[1]);
await assert.rejects(() => add(input, { config: options.config, utils: { fetch: async () => ({ ok: false, json: async () => ({ message: "口令错误" }) }) } }), /口令错误/);
console.log("PASS: Manggo plugin payload, duplicate feedback, configuration errors and safe retry (9 checks).");
