// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Meltype の不具合報告フォーム (Google フォーム) の Apps Script。
// フォームが送信されたら、その内容を GitHub の Issue にする (GitHub のアカウントが無い人も報告できるように)。
// Issue の本文は .github/ISSUE_TEMPLATE と同じ「### 見出し + 値」の形にするので、GitHub の bot がそのまま仕分け・再現できる。
// 設定の手順は tools/report-form/README.md。
//
// スクリプト プロパティ (プロジェクトの設定 → スクリプト プロパティ) に次を登録する:
//   GITHUB_TOKEN  … Meltype のリポジトリの Issues だけ書き込める Fine-grained personal access token
//   GITHUB_REPO   … yksr-melt/Meltype

/** フォームの質問のタイトル (README.md の表と同じにする)。 */
const Q = {
  kind: '報告の種類',
  os: 'OS',
  version: 'Meltype の版',
  app: 'どのアプリで',
  typed: '打ったもの (キーをそのまま)',
  actual: '出たもの',
  expected: '期待した結果',
  lastKey: '最後に押したキー',
  context: '前後の文 (分かれば)',
  steps: '何をしたら',
  result: 'どうなったか',
  detail: '内容',
  log: 'ログ (あれば)',
  env: '実行環境 (自動で入ります)',
};

/** 報告の種類 → Issue のラベル・タイトルの頭。 */
const KINDS = {
  '不具合': { label: 'bug', prefix: '[不具合]' },
  '変換・判定の間違い': { label: '誤判定', prefix: '[誤判定]' },
  '辞書に追加してほしい語': { label: '辞書', prefix: '[辞書]' },
  '改善の提案': { label: '提案', prefix: '[提案]' },
};

/** 1 つの欄の長さの上限 (長すぎる送信で Issue を作れなくならないように)。 */
const MAX_FIELD = 4000;

function onFormSubmit(e) {
  const answers = {};
  for (const [title, values] of Object.entries(e.namedValues)) {
    answers[title.trim()] = values.join(', ').trim().slice(0, MAX_FIELD);
  }
  const get = key => answers[Q[key]] || '';
  const kind = KINDS[get('kind')] || KINDS['不具合'];

  // 見出しの順番は .github/ISSUE_TEMPLATE のフォームと同じ。空の欄は GitHub のフォームと同じく _No response_。
  const sections = [
    ['種類', get('kind')],
    [Q.os, get('os')],
    [Q.version, get('version')],
    [Q.app, get('app')],
  ];
  if (kind.label === '誤判定') {
    sections.push([Q.typed, get('typed')], [Q.actual, get('actual')], [Q.expected, get('expected')], [Q.lastKey, get('lastKey')], [Q.context, get('context')]);
  } else if (kind.label === 'bug') {
    sections.push(['何をしたら', get('steps')], ['どうなったか', get('result')], ['どうなってほしかったか', get('expected')]);
  } else {
    sections.push([Q.detail, get('detail')]);
  }
  const block = text => '```text\n' + text.replace(/```/g, 'ˋˋˋ') + '\n```';
  const log = get('log');
  const env = get('env');
  const body = [
    ...sections.map(([title, value]) => `### ${title}\n\n${value || '_No response_'}`),
    `### ${Q.log}\n\n${log ? block(log) : '_No response_'}`,
    // Meltype のメニューから開いたときに自動で入る実行環境 (Windows の版・モード・キーボードなど)。長いので畳んでおく。
    `### 実行環境\n\n${env ? '<details><summary>開く</summary>\n\n' + block(env) + '\n</details>' : '_No response_'}`,
    '---\n<sub>Google フォームから送られた報告です (報告した人は GitHub のコメントを見られないことがあります)。</sub>',
  ].join('\n\n');

  const summary = (get('typed') || get('result') || get('detail') || get('actual')).split('\n')[0].slice(0, 60);
  createIssue(`${kind.prefix} ${summary}`.trim(), body, [kind.label, 'フォームから']);
}

function createIssue(title, body, labels) {
  const props = PropertiesService.getScriptProperties();
  const token = props.getProperty('GITHUB_TOKEN');
  const repo = props.getProperty('GITHUB_REPO') || 'yksr-melt/Meltype';
  const response = UrlFetchApp.fetch(`https://api.github.com/repos/${repo}/issues`, {
    method: 'post',
    contentType: 'application/json',
    headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json' },
    payload: JSON.stringify({ title, body, labels }),
    muteHttpExceptions: true,
  });
  if (response.getResponseCode() >= 300) {
    // 失敗しても回答はスプレッドシートに残る。実行ログで理由を見られるようにする。
    throw new Error(`Issue を作れませんでした: ${response.getResponseCode()} ${response.getContentText()}`);
  }
}
