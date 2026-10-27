cask "meltype-for-mac" do
  version "1.2.5"
  sha256 "c60ea3570334460cae0f8b5899e8ae351181724944700c1e037bd578fd4e17f1"

  url "https://github.com/whitewater-png/Meltype-for-Mac/releases/download/v#{version}-mac/Meltype-mac-#{version}.zip"
  name "Meltype for Mac"
  desc "Japanese input method with English-aware conversion"
  homepage "https://github.com/whitewater-png/Meltype-for-Mac"

  livecheck do
    url :url
    regex(/^v?(\d+(?:\.\d+)+)-mac$/i)
    strategy :github_releases do |json, regex|
      json.map do |release|
        next if release["draft"] || release["prerelease"]

        release["tag_name"]&.[](regex, 1)
      end
    end
  end

  depends_on arch: :arm64
  # 実際の最低 OS は 13.3 (llama.framework の minos)。cask は :ventura (13) 単位でしか書けないので、13.0〜13.2 は caveats で案内する。
  depends_on macos: :ventura

  input_method "Meltype-mac/Meltype.app"

  postflight_steps do
    # quarantine 属性を外す。pkg で入れたときは付かないので、それと同じ結果にするため。
    # 付いたままだと Gatekeeper が ad-hoc 署名の IME を黙って止める (起動は imklaunchagent が裏で行うので
    # 「開発元が未確認」の表示も出ず、入力メニューに出ないだけになる。--register も同じ理由で失敗する)。
    # run の args は base で解決されず、サンドボックスの中では $HOME も偽のホームを指す (実機で確認)。
    # args で使える置き換えは {{user}} などに限られるので、/Users/{{user}} で本物のホームを指す
    # (ホームが /Users/<ユーザー名> でない構成は対象外)。
    run "/usr/bin/xattr",
        args:           ["-dr", "com.apple.quarantine", "/Users/{{user}}/Library/Input Methods/Meltype.app"],
        must_succeed:   false,
        writable_paths: ["Library/Input Methods/Meltype.app"],
        writable_base:  :home
    # 動いている IME と辞書の管理画面を止める (入れ替えのとき。無ければ無視)
    terminate_process "Meltype"
    terminate_process "Input Methods/Meltype.app/Contents/Helpers/MeltypeDictionary.app/", match: :full
    # 入力ソースとして登録し、入力メニューと IME の起動役を起動し直す (失敗しても caveats で案内)
    run "Library/Input Methods/Meltype.app/Contents/MacOS/Meltype",
        args:         ["--register"],
        base:         :home,
        must_succeed: false
    terminate_process "imklaunchagent"
    terminate_process "TextInputMenuAgent"
  end

  uninstall_preflight_steps do
    # 入力ソースを外してから、動いている Meltype と辞書の管理画面を止める (mac/uninstall.sh と同じ)
    run "Library/Input Methods/Meltype.app/Contents/MacOS/Meltype",
        args:         ["--unregister"],
        base:         :home,
        must_succeed: false
    terminate_process "Meltype"
    terminate_process "Input Methods/Meltype.app/Contents/Helpers/MeltypeDictionary.app/", match: :full
  end

  zap trash: [
    "~/Library/Application Support/Meltype",
    "~/Library/Preferences/io.github.yksr-melt.inputmethod.Meltype.plist",
  ]

  caveats <<~EOS
    macOS 13.3 以降が必要です (13.0〜13.2 では Meltype が起動しません)。

    インストール後は、いったんログアウトしてログインし直してください (Homebrew の処理はサンドボックスの中で動くため、
    入力ソースの登録を済ませられません。ログアウトせずに使うには、次を実行してから入力メニューで Meltype を選びます)。
      "$HOME/Library/Input Methods/Meltype.app/Contents/MacOS/Meltype" --register

    署名は ad-hoc で、Apple の公証は受けていません。そのまま起動できるよう、入れるときに quarantine 属性を外しています
    (pkg で入れたときと同じ結果です。Release の SHA-256 は Homebrew が検証します)。

    zip や開発版の Meltype.app が既に ~/Library/Input Methods にあると、Homebrew は入れません。
    先に uninstall.sh か Finder で外してから brew install してください。

    アプリ内の「新しい版があります」の通知は、Homebrew の版と食い違うことがあります。
    更新は brew upgrade --cask meltype-for-mac で行ってください。

    設定・学習データ (~/Library/Application Support/Meltype) は brew uninstall では消えません。
    消すときは brew uninstall --zap --cask meltype-for-mac です (ユーザー辞書は先に控えを取ってください)。
  EOS
end
