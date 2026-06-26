# Voice Chat Launcher

OpenWakeWord でウェイクワードを待ち受け、ChatGPT Windows アプリを開いて音声ボタンを押す常駐アプリです。

## 前提条件

- Windows
- ChatGPT Windows アプリ
- .NET Framework 4.x の C# コンパイラ
  - 通常は Windows に含まれる `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` を使います。
- Python 3
  - `setup_openwakeword.ps1` が `.venv` を作成し、OpenWakeWord などをインストールします。
- マイク
  - Windows のマイク権限で、デスクトップアプリからのアクセスを許可してください。

## 使い方

1. 初回だけ `setup_openwakeword.ps1` を実行します。
2. `build.ps1` を実行します。
3. `bin\VoiceChatLauncher.exe` を起動します。
4. 起動後はウェイクワードを待ち受けます。
5. 既定では `Hey Lucy` と言うと、ChatGPT を探して前面に出し、音声ボタンを押します。

うまく動かない場合は `diagnose.ps1` を実行すると、利用できる音声認識エンジンと ChatGPT アプリの登録名を確認できます。

## ビルド

初回セットアップ:

```powershell
.\setup_openwakeword.ps1
```

アプリのビルド:

```powershell
.\build.ps1
```

ビルド結果:

```text
bin\VoiceChatLauncher.exe
bin\config.ini
```

`build.ps1` は、既に `bin\config.ini` がある場合は上書きしません。設定を初期化したい場合は `bin\config.ini` を削除してから再ビルドしてください。

## 設定

ビルド後の設定は `bin\config.ini` で変更できます。

- `ListenEngine`: `openwakeword` なら OpenWakeWord、`windows` なら従来の Windows 音声認識を使います。
- `OpenWakeWordModels`: 待ち受けるモデルです。標準では `..\models\Hey_Lucy_20260609_095011.onnx` です。
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

このリポジトリでは `models\Hey_Lucy_20260609_095011.onnx` を使う設定になっています。

### WakeWord を変更する

標準モデルを使う場合は、`bin\config.ini` の `OpenWakeWordModels` をモデル名に変更します。

```ini
OpenWakeWordModels=hey_jarvis
```

複数モデルを同時に待ち受ける場合は `|` で区切ります。

```ini
OpenWakeWordModels=hey_jarvis|alexa
```

### ONNX ファイルを追加する

OpenWakeWord 用に作成された `.onnx` ファイルを `models` フォルダに置きます。

```text
models\my_wakeword.onnx
```

その後、`bin\config.ini` を変更します。

```ini
OpenWakeWordModels=..\models\my_wakeword.onnx
```

`VoiceChatLauncher.exe` は `bin` フォルダから動くため、プロジェクト直下の `models` を指すには `..\models\...` と指定します。絶対パスも使えます。

```ini
OpenWakeWordModels=C:\path\to\my_wakeword.onnx
```

### 反応しやすさを調整する

```ini
OpenWakeWordThreshold=0.80
```

- 反応しにくい場合は `0.70` などへ下げます。
- 誤反応が多い場合は `0.90` などへ上げます。

マイク一覧を確認したい場合:

```powershell
.\.venv\Scripts\python.exe .\scripts\openwakeword_listener.py --list-devices
```

直接テストしたい場合:

```powershell
.\.venv\Scripts\python.exe .\scripts\openwakeword_listener.py --models .\models\Hey_Lucy_20260609_095011.onnx --threshold 0.80 --log-scores
```

## 注意

- ChatGPT アプリにログイン済みで、マイク権限が許可されている必要があります。
- Windows の「デスクトップ アプリがマイクにアクセスできるようにする」がオフだと待ち受けできません。
- OpenWakeWord を使う場合、Windows の音声認識と言語パックは不要です。
- ChatGPT アプリの画面構造が変わるとボタン検出に失敗する場合があります。その場合は `bin\voice-command.log` を見て、`VoiceButtonNames` や `WindowTitleKeyword` を調整してください。
