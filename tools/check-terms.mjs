// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// 専門用語集 (dictionaries/terms-*.txt) の品質を検査する。
//   node tools/check-terms.mjs [ファイル …]   … 省略すると dictionaries/terms-*.txt をすべて
//   node tools/check-terms.mjs --self-test    … この検査自身の自己テスト
// エラーがあれば終了コード 1。警告・情報は表示だけ (終了コードは 0)。
// 読み込む側の決まり (src/Meltype.Core/Composition/TermDictionary.cs、UserDictionary.Validate) と、ここでの上乗せの決まり:
//   エラー  先頭の `# 名称:` (入力メニュー「専門用語集」に出る分野の名前) `# 出典:` `# ライセンス:` の行が無い / 欄の数が 2〜3 でない / 読みがひらがな (ぁ-ゖ・ゔ・長音ー) だけでない
//           (カタカナ・数字・英字・中点「・」・空白は不可) / 読みが 2 文字未満・100 文字超 / 語が空・100 文字超
//   警告    同じ読み+語の重複 (読み込みでは 1 つにまとめられる) / 同じ読みに語が 20 を超える / 欄の前後に空白 / 先頭が長音の読み / 語が読みと同じ / BOM
//   情報    読みが 3 文字以下の語と、語が ASCII だけの語 (読みが長くても) は「候補追加型」(変換候補に足すだけ。強制しない) になる / 総語数・強制型の数
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const MAX_SAME_READING = 20;
export const FORCED_MIN_READING_LENGTH = 4; // TermDictionary.ForcedMinReadingLength と同じ
const MIN_READING = 2;
const MAX_LENGTH = 100;
const readingChars = /^[ぁ-ゖゔー]+$/;

/**
 * 1 つのファイルの中身を検査する。seen (同じ読み+語) と readings (読みごとの語の数) は複数ファイルをまたいで共有する。
 * 戻り値: { problems: [{ level, line, message }], stats }
 */
export function checkText(text, seen = new Map(), readings = new Map()) {
  const problems = [];
  const add = (level, line, message) => problems.push({ level, line, message });
  const stats = { words: 0, forced: 0, candidate: 0, skipped: 0, four: 0, ascii: 0 };
  const fourSamples = [];
  let placeholder = false;
  if (text.charCodeAt(0) === 0xfeff) {
    add('warning', 1, '先頭に BOM がある');
    text = text.slice(1);
  }
  let header = true; // 先頭のコメントの並び
  let source = false, license = false, label = false;
  const lines = text.split('\n');
  lines.forEach((raw, i) => {
    const lineNo = i + 1;
    const line = raw.replace(/\r$/, '');
    if (line.trim() === '' ) { return; }
    if (line.trimStart().startsWith('#')) {
      if (header) {
        if (/^#\s*名称[:：]\s*\S/.test(line)) label = true;
        if (/^#\s*出典[:：]\s*\S/.test(line)) source = true;
        if (/^#\s*ライセンス[:：]\s*\S/.test(line)) license = true;
        if (/^#\s*(名称|出典|ライセンス)[:：].*[(（]例/.test(line)) placeholder = true;
      }
      return;
    }
    header = false;
    const fields = line.split('\t');
    if (fields.length < 2 || fields.length > 3) {
      add('error', lineNo, `タブ区切りの欄が ${fields.length} 個 (読み<Tab>語<Tab>注記(任意) の 2〜3 個のはず)`);
      stats.skipped++;
      return;
    }
    if (fields.some(f => f !== f.trim())) add('warning', lineNo, '欄の前後に空白がある (読み込みでは取り除かれる)');
    const reading = fields[0].trim();
    const word = fields[1].trim();
    let bad = false;
    if (reading.length < MIN_READING) { add('error', lineNo, `読み「${reading}」が ${MIN_READING} 文字未満`); bad = true; }
    else if (reading.length > MAX_LENGTH) { add('error', lineNo, `読みが ${MAX_LENGTH} 文字を超えている (${reading.length} 文字)`); bad = true; }
    if (reading.length > 0 && !readingChars.test(reading)) {
      const found = [...new Set([...reading].filter(c => !/[ぁ-ゖゔー]/.test(c)))].join('');
      const hint = /[ァ-ヺ]/.test(found) ? ' (カタカナはひらがなに)' : /[0-9０-９a-zA-Zａ-ｚＡ-Ｚ]/.test(found) ? ' (数字・英字は読みに使えない)' : /・/.test(found) ? ' (中点「・」は読みに使えない)' : '';
      add('error', lineNo, `読み「${reading}」にひらがな・長音ー以外の文字「${found}」がある${hint}`);
      bad = true;
    }
    if (word.length === 0) { add('error', lineNo, '語が空'); bad = true; }
    else if (word.length > MAX_LENGTH) { add('error', lineNo, `語が ${MAX_LENGTH} 文字を超えている (${word.length} 文字)`); bad = true; }
    if (bad) { stats.skipped++; return; }
    if (reading.startsWith('ー')) add('warning', lineNo, `読み「${reading}」が長音で始まっている`);
    if (word === reading) add('warning', lineNo, `語が読みと同じ (「${word}」)`);
    const key = `${reading}\t${word}`;
    if (seen.has(key)) { add('warning', lineNo, `${seen.get(key)} と同じ読み+語 (「${reading}」→「${word}」)`); return; }
    seen.set(key, `${lineNo} 行目`);
    readings.set(reading, (readings.get(reading) ?? 0) + 1);
    stats.words++;
    // 語が ASCII だけの語は、読みが長くても候補追加型 (TermDictionary.IsAscii と同じ。日常語を英単語に置き換えないため)
    const ascii = /^[\x00-\x7f]+$/.test(word);
    if (ascii && reading.length >= FORCED_MIN_READING_LENGTH) stats.ascii++;
    if (reading.length >= FORCED_MIN_READING_LENGTH && !ascii) stats.forced++; else stats.candidate++;
    if (reading.length === FORCED_MIN_READING_LENGTH && !ascii) { stats.four++; if (fourSamples.length < 5) fourSamples.push(`${reading}→${word}`); }
  });
  if (!label) add('error', 1, '先頭のコメントに「# 名称: …」の行が無い (入力メニュー「専門用語集」に出る分野の名前。例: # 名称: 土木・建設)');
  if (!source) add('error', 1, '先頭のコメントに「# 出典: …」の行が無い (自作なら「# 出典: 自作」でよい)');
  if (!license) add('error', 1, '先頭のコメントに「# ライセンス: …」の行が無い (自作なら「# ライセンス: 自作」でよい)');
  if (stats.candidate > 0) add('info', 0, `読みが ${FORCED_MIN_READING_LENGTH - 1} 文字以下の ${stats.candidate - stats.ascii} 語と、語が ASCII だけの ${stats.ascii} 語 (読みが ${FORCED_MIN_READING_LENGTH} 文字以上でも) は「候補追加型」になる (変換候補に足すだけで、強制しない)`);
  if (stats.four > 0) add('info', 0, `読みがちょうど ${FORCED_MIN_READING_LENGTH} 文字 (強制型の最短) の語が ${stats.four} 語ある。日常語の途中に現れて巻き込む恐れがあるので注意 (例: ${fourSamples.join('、')})`);
  if (placeholder && stats.words > 0) add('warning', 1, '名称・出典・ライセンスの行が、雛形の「(例: …)」のまま (実際の内容に書き換える)');
  return { problems, stats };
}

/** 読みごとの語の数が多すぎるものを警告にする (複数ファイルの合計で数える)。 */
export function tooMany(readings) {
  return [...readings].filter(([, n]) => n > MAX_SAME_READING).map(([reading, n]) => ({ level: 'warning', line: 0, message: `読み「${reading}」に語が ${n} 個ある (${MAX_SAME_READING} 個以下を目安に)` }));
}

function run(files) {
  const seen = new Map();
  const readings = new Map();
  let errors = 0, warnings = 0;
  const total = { words: 0, forced: 0, candidate: 0, skipped: 0, four: 0 };
  const out = [];
  for (const file of files) {
    const { problems, stats } = checkText(fs.readFileSync(file, 'utf8'), seen, readings);
    const shown = path.relative(process.cwd(), file) || file;
    for (const p of problems) {
      const where = `file=${shown}${p.line > 0 ? `,line=${p.line}` : ''}`;
      if (p.level === 'error') errors++;
      else if (p.level === 'warning') warnings++;
      out.push(`::${p.level === 'info' ? 'notice' : p.level} ${where}::${p.message}`);
    }
    for (const k of Object.keys(total)) total[k] += stats[k];
    out.push(`${path.basename(file)}: ${stats.words} 語 (強制型 ${stats.forced} / 候補追加型 ${stats.candidate})`);
  }
  for (const p of tooMany(readings)) { warnings++; out.push(`::warning::${p.message}`); }
  console.log(out.join('\n'));
  console.log(`専門用語集: ${files.length} ファイル、総語数 ${total.words} (強制型 ${total.forced} / 候補追加型 ${total.candidate})、飛ばす行 ${total.skipped}、エラー ${errors}、警告 ${warnings}`);
  return errors > 0 ? 1 : 0;
}

function selfTest() {
  const header = '# 名称: テスト\n# 出典: 自作\n# ライセンス: 自作\n';
  const levels = (text, level) => checkText(text).problems.filter(p => p.level === level).map(p => p.message);
  const cases = [
    ['読みがちょうど 4 文字は情報', () => levels(header + 'こうけつ\t硬結\nぎょうれつしき\t行列式\n', 'info').some(m => m.includes('ちょうど 4 文字'))],
    ['雛形の (例: が残り、語があれば警告', () => checkText('# 名称: テスト\n# 出典: (例: 自作)\n# ライセンス: 自作\nぎょうれつしき\t行列式\n').problems.some(p => p.level === 'warning' && p.message.includes('雛形'))],
    ['雛形のままでも語 0 件なら警告なし', () => !checkText('# 名称: (例: 土木)\n# 出典: (例: 自作)\n# ライセンス: (例: 自作)\n').problems.some(p => p.level === 'warning')],
    ['正常な用語集はエラーなし', () => levels(header + 'ぎょうれつしき\t行列式\t数学\n', 'error').length === 0],
    ['統計 (強制型・候補追加型)', () => { const s = checkText(header + 'ぎょうれつしき\t行列式\nすう\t数\n').stats; return s.words === 2 && s.forced === 1 && s.candidate === 1; }],
    ['語が ASCII だけなら読みが長くても候補追加型', () => { const s = checkText(header + 'あいこんをくりっく\ticon\nぎょうれつしき\t行列式\n').stats; return s.forced === 1 && s.candidate === 1 && s.ascii === 1; }],
    ['3 文字以下は候補追加型の情報', () => levels(header + 'すう\t数\n', 'info').some(m => m.includes('候補追加型'))],
    ['名称が無いとエラー', () => levels('# 出典: 自作\n# ライセンス: 自作\nぎょうれつしき\t行列式\n', 'error').some(m => m.includes('名称'))],
    ['名称の値が空だとエラー', () => levels('# 名称:\n# 出典: 自作\n# ライセンス: 自作\n', 'error').some(m => m.includes('名称'))],
    ['出典が無いとエラー', () => levels('# 名称: テスト\n# ライセンス: 自作\nぎょうれつしき\t行列式\n', 'error').some(m => m.includes('出典'))],
    ['ライセンスが無いとエラー', () => levels('# 名称: テスト\n# 出典: 自作\nぎょうれつしき\t行列式\n', 'error').some(m => m.includes('ライセンス'))],
    ['出典の値が空だとエラー', () => levels('# 名称: テスト\n# 出典:\n# ライセンス: 自作\n', 'error').some(m => m.includes('出典'))],
    ['語の後ろの出典は先頭と数えない', () => levels('# 名称: テスト\n# ライセンス: 自作\nぎょうれつしき\t行列式\n# 出典: 自作\n', 'error').some(m => m.includes('出典'))],
    ['カタカナの読みはエラー', () => levels(header + 'ギョウレツ\t行列\n', 'error').some(m => m.includes('カタカナ'))],
    ['数字・英字・中点の読みはエラー', () => ['ぎょう1れつ', 'ぎょうAれつ', 'ぎょう・れつ'].every(r => levels(header + `${r}\t行列\n`, 'error').length === 1)],
    ['長音ーは許容', () => levels(header + 'こんぴゅーたー\tコンピューター\n', 'error').length === 0],
    ['読みが 1 文字はエラー', () => levels(header + 'あ\t亜\n', 'error').length === 1],
    ['読みが 101 文字はエラー', () => levels(header + 'あ'.repeat(101) + '\t語\n', 'error').length === 1],
    ['語が 101 文字はエラー', () => levels(header + 'ぎょうれつ\t' + '語'.repeat(101) + '\n', 'error').length === 1],
    ['欄が足りない行はエラー', () => levels(header + 'ぎょうれつしき 行列式\n', 'error').length === 1],
    ['欄が多すぎる行はエラー', () => levels(header + 'ぎょうれつ\t行列\t注記\t余分\n', 'error').length === 1],
    ['同じ読み+語は警告', () => levels(header + 'ぎょうれつ\t行列\nぎょうれつ\t行列\n', 'warning').some(m => m.includes('同じ読み+語'))],
    ['同じ読みで語が 21 個は警告', () => { const readings = new Map(); checkText(header + Array.from({ length: 21 }, (_, i) => `ぎょうれつ\t語${i}`).join('\n') + '\n', new Map(), readings); return tooMany(readings).length === 1; }],
    ['同じ読みで語が 20 個は警告なし', () => { const readings = new Map(); checkText(header + Array.from({ length: 20 }, (_, i) => `ぎょうれつ\t語${i}`).join('\n') + '\n', new Map(), readings); return tooMany(readings).length === 0; }],
    ['CRLF でも読める', () => levels(header.replace(/\n/g, '\r\n') + 'ぎょうれつしき\t行列式\r\n', 'error').length === 0],
  ];
  let failed = 0;
  for (const [name, test] of cases) {
    let ok = false;
    try { ok = test(); } catch (e) { console.log(`  例外: ${e.message}`); }
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}`);
    if (!ok) failed++;
  }
  // 実際のファイル経由でも終了コードを確かめる
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'check-terms-'));
  try {
    const good = path.join(dir, 'terms-good.txt');
    const bad = path.join(dir, 'terms-bad.txt');
    fs.writeFileSync(good, header + 'ぎょうれつしき\t行列式\n');
    fs.writeFileSync(bad, 'ぎょうれつしき\t行列式\n');
    const origLog = console.log;
    console.log = () => {};
    const goodCode = run([good]);
    const badCode = run([bad]);
    const missingCode = runArgs([path.join(dir, 'no-such-file.txt')]);
    console.log = origLog;
    const ok = goodCode === 0 && badCode === 1 && missingCode === 1;
    console.log(`${ok ? 'PASS' : 'FAIL'}  終了コード (正常 0 / エラー 1 / 存在しない引数 1)`);
    if (!ok) failed++;
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
  console.log(`自己テスト: ${cases.length + 1 - failed}/${cases.length + 1}`);
  return failed > 0 ? 1 : 0;
}

/** 引数のファイルを検査する。存在しない引数はエラー (黙って除外すると、打ち間違いが exit 0 になる)。 */
function runArgs(args) {
  const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'dictionaries');
  const files = [];
  let missing = 0;
  for (const arg of args) {
    const found = [arg, path.join(root, path.basename(arg))].find(f => fs.existsSync(f));
    if (found) files.push(found);
    else { console.log(`::error::ファイルが見つからない: ${arg}`); missing++; }
  }
  return Math.max(run(files), missing > 0 ? 1 : 0);
}

function isDirectRun() {
  try {
    return !!process.argv[1] && fs.realpathSync(process.argv[1]) === fs.realpathSync(fileURLToPath(import.meta.url));
  } catch {
    return false;
  }
}

if (isDirectRun()) {
  const args = process.argv.slice(2);
  if (args[0] === '--self-test') process.exitCode = selfTest();
  else if (args.length > 0) process.exitCode = runArgs(args);
  else {
    const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'dictionaries');
    process.exitCode = run(fs.readdirSync(root).filter(f => /^terms-.*\.txt$/.test(f)).sort().map(f => path.join(root, f)));
  }
}
