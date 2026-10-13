export async function add(request, { config, utils }) {
  const token = String(config.token || "").trim();
  if (!token) throw new Error("请先在拾词设置中复制连接口令，填入此插件的配置。");
  const word = String(request.sourceText || "").trim();
  if (!word) throw new Error("请先选中一个英文单词或短语。");
  const dictionary = request.translationResult?.dictionary;
  const body = JSON.stringify({
    word,
    meaning: String(request.translatedText || "").slice(0, 4000),
    phonetic: String(dictionary?.pronunciations?.find(p => p.phonetic)?.phonetic || "").slice(0, 4000),
    partOfSpeech: String(dictionary?.meanings?.[0]?.partOfSpeech || "").slice(0, 4000),
  });
  // Reuse the ID so the running app can recognize a retried request.
  const requestId = crypto.randomUUID();
  let response;
  for (let attempt = 0; attempt < 2; attempt++) {
    try {
      response = await utils.fetch("http://127.0.0.1:17653/words", {
        method: "POST",
        headers: { "Authorization": `Bearer ${token}`, "Content-Type": "application/json", "X-Request-Id": requestId },
        body,
        signal: AbortSignal.timeout(7000),
      });
      break;
    } catch {
      if (attempt === 1) throw new Error("没有连接到拾词。请先启动 MyWords.exe，并确认托盘里有拾词图标。");
    }
  }
  let result;
  try { result = await response.json(); }
  catch { throw new Error("拾词返回了无效结果，请重新打开拾词后重试。"); }
  if (!response.ok || !result.success) throw new Error(result.message || "收藏失败，请检查拾词设置。");
  return { success: true, message: result.message };
}
