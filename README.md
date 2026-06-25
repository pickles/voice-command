# Voice Chat Launcher

OpenWakeWord でウェイクワードを待ち受け、ChatGPT Windows アプリを開いて音声ボタンを押す常駐アプリです。

## 使い方

1. 初回だけ `setup_openwakeword.ps1` を実行します。
2. `build.ps1` を実行します。
3. `bin\VoiceChatLauncher.exe` を起動します。
4. 起動後はウェイクワードを待ち受けます。
5. 音声で `hey jarvis` と言うと、ChatGPT を探して前面に出し、音声ボタンを押します。

うまく動かない場合は `diagnose.ps1` を実行すると、利用できる音声認識エンジンと ChatGPT アプリの登録名を確認できます。

## 設定

ビルド後の設定は `bin\config.ini` で変更できます。

- `ListenEngine`: `openwakeword` なら OpenWakeWord、`windows` なら従来の Windows 音声認識を使います。
- `OpenWakeWordModels`: 待ち受けるモデルです。標準では `hey_jarvis` です。
- `OpenWakeWordThreshold`: 反応のしきい値です。誤反応が多い場合は上げ、反応しにくい場合は下げます。
- `OpenWakeWordDevice`: マイクを明示指定したい場合にデバイス番号を入れます。
- `Keywords`: Windows 音声認識を使う場合の合図です。複数指定は `|` で区切ります。
- `EnableDictationFallback`: 完全一致のキーワード認識に失敗した場合、自由認識した文章からキーワードを探します。
- `LaunchCommand`: ChatGPT の起動方法です。`chatgpt:` で起動しない場合は、ChatGPT アプリの実行ファイル、または `shell:AppsFolder\<AppID>` を指定します。
- `ProcessNames`: 既に起動中の ChatGPT を探すためのプロセス名です。`.exe` 付きでも指定できます。
- `MinimumWindowWidth` / `MinimumWindowHeight`: ChatGPT の小さな補助ウィンドウを誤検出しないための最小サイズです。
- `CenterWindowOnForeground`: ChatGPT を前面に出す時、現在の画面中央へ移動します。
- `RunActionOnStartup`: `true` にすると、Voice Chat Launcher 起動時に自動で一度 ChatGPT の音声ボタンを押します。
- `AfterBringToFrontDelayMilliseconds`: ChatGPT を復元して前面に出したあと、ボタンを探すまでの待ち時間です。
- `VoiceButtonNames`: ChatGPT 画面内で探す音声ボタン名です。アプリの表示言語や更新で変わった場合はここを直します。
- `RightmostVoiceButtonFallback`: UI自動操作で見える右下ボタンを押す旧方式です。プロフィールメニューを誤クリックしやすいため通常は `false` にします。
- `CoordinateFallbackEnabled`: ChatGPT がボタン情報を公開しない場合に、画面上の位置で `音声を使用する` ボタンを押します。
- `CoordinateFallbackBottomOffset`: 位置クリックが上下にずれる場合に調整します。大きくすると上、小さくすると下をクリックします。
- `CoordinateFallbackRightOffset`: 位置クリックが左右にずれる場合に調整します。大きくすると左、小さくすると右をクリックします。

## OpenWakeWord

標準で使える WakeWord は `hey_jarvis`, `alexa`, `hey_mycroft`, `hey_rhasspy`, `timer`, `weather` です。

マイク一覧を確認したい場合:

```powershell
.\.venv\Scripts\python.exe .\scripts\openwakeword_listener.py --list-devices
```

直接テストしたい場合:

```powershell
.\.venv\Scripts\python.exe .\scripts\openwakeword_listener.py --models hey_jarvis --log-scores
```

## 注意

- ChatGPT アプリにログイン済みで、マイク権限が許可されている必要があります。
- Windows の「デスクトップ アプリがマイクにアクセスできるようにする」がオフだと待ち受けできません。
- OpenWakeWord を使う場合、Windows の音声認識と言語パックは不要です。
- ChatGPT アプリの画面構造が変わるとボタン検出に失敗する場合があります。その場合は `bin\voice-command.log` を見て、`VoiceButtonNames` や `WindowTitleKeyword` を調整してください。
