# Voice Chat Launcher

OpenWakeWord でウェイクワードを待ち受け、ChatGPT Windows アプリを開いて音声ボタンを押す常駐アプリです。

## 前提条件

- Windows
- ChatGPT Windows アプリ
- .NET Framework 4.x の C# コンパイラ
  - 通常は Windows に含まれる `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` を使います。
- .NET SDK
  - `setup_openwakeword.ps1` と `build.ps1` が ONNX Runtime の NuGet パッケージを復元します。
- マイク
  - Windows のマイク権限で、デスクトップアプリからのアクセスを許可してください。

## 使い方

1. 初回だけ `setup_openwakeword.ps1` を実行します。
2. `build.ps1` を実行します。
3. `bin\VoiceChatLauncher.exe` を起動します。
4. 起動後はウェイクワードを待ち受けます。
5. 既定では `Hey Lucy` と言うと、新しい ChatGPT で通常の新規チャットを開き、音声開始ボタンを押します。

うまく動かない場合は `diagnose.ps1` を実行すると、ChatGPT アプリの登録名を確認できます。

## 新しい ChatGPT への移行

`UseNewChatGpt=true` が既定です。既存の `bin\config.ini` にこの項目がなくても新版を使います。設定ファイルやマイク・ウェイクワード設定を上書きする必要はありません。ビルド後、実行中のランチャーを終了して新しい実行ファイルを起動してください。

Microsoft Store 版の新しい ChatGPT（パッケージ `OpenAI.Codex_2p2nqsd0c76g0`）を対象とします。毎回 `codex://threads/new?mode=chat` を開くため、Work / Codex の作業中でも通常の新規チャットで始めます。前面ウィンドウのパッケージを検査し、Classic と区別します。日本語の「新しい音声チャットを開始」または英語の `Start new voice chat` が表示され、有効で、UI Automation の Invoke 操作を公開している場合だけ押します。画面を他のアプリへ切り替えた場合は中止します。

この経路はインストール済み ChatGPT 26.1103.4239 の実装と日本語リソースで確認しました。公開APIではないため、更新で変わる可能性があります。音声機能の利用可否やマイク権限は ChatGPT 側で確認してください。[公式の音声機能案内](https://learn.chatgpt.com/docs/features/voice)。音声入力（文字起こし）と音声チャットは別機能です。

新版では `LaunchCommand` / `LaunchArguments`、ウィンドウのタイトル・プロセス名・位置設定、`VoiceButtonNames` / `ExcludedButtonNames`、位置クリックの設定を使いません。`StartupDelayMilliseconds`、`AfterBringToFrontDelayMilliseconds`、`WindowTimeoutMilliseconds`、`ButtonTimeoutMilliseconds` は共通です。設定画面「起動」の新版チェックを外すか `UseNewChatGpt=false` を指定すると、従来の Classic 用設定で動作します。

動作確認はトレイの「今すぐ起動」で行ってください。通常の新規チャットで音声が始まることを確認して終了し、続いて `Hey Lucy` で同じ動作を確認します。Classic 同時起動時と、Work / Codex 表示中でも確認してください。ログの `New ChatGPT voice button invoked` はボタン操作の完了を示し、音声接続の成功を保証するものではありません。

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

### Windows サインイン時の自動起動

トレイの `設定` → `自動起動` → `Windows サインイン時に自動起動する` にチェックを入れて `保存` すると、次回の Windows サインインから待ち受けを開始します。解除すると自動起動登録を削除します。初期状態は未登録です。登録状態と起動先は同じ画面で確認できます。

現在のユーザーの `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` に `VoiceChatLauncher` という値を登録します。管理者権限は不要です。この設定は Windows の登録状態から読み取り、`config.ini` には保存しません。読み取り・変更が失敗した場合は設定画面にエラーを表示します。

Windows のスタートアップ設定で無効化している場合は、そちらでも有効にしてください。実行ファイルを移動した場合は移動先から設定を開いて保存し直してください。アプリを削除する前にチェックを外して保存してください。`起動` タブの「起動時に音声ボタンを押す」は別の設定です。通常はオフのままにすると、サインイン時はウェイクワードの待ち受けだけを始めます。

実際のサインイン時の起動確認は、登録を有効にしてから次回のサインイン時にトレイアイコンが現れることを確認してください。

### 音声と起動操作の設定

起動後、タスクトレイの Voice Chat Launcher アイコンを右クリックして `設定` を開くと、Wake Word、ChatGPT の起動、ウィンドウ検出、音声ボタン検出、位置クリックの設定を変更できます。
保存すると `bin\config.ini` に反映され、待ち受けが新しい設定で再起動します。

同じタスクトレイメニューから `音声認識を一時停止` を選ぶと待ち受けを止められます。一時停止中は音声コマンドを処理しません。再開する場合は `音声認識を再開` を選びます。`状態を表示` で現在の状態とログを通常ウィンドウで確認できます。

同じ内容は `bin\config.ini` を直接編集しても変更できます。
ログは `bin\voice-command.log` に出力され、24時間より古い行は起動時と実行中に自動で削除されます。

- `OpenWakeWordModels`: 待ち受けるモデルです。標準では `..\models\Hey_Lucy_20260609_095011.onnx` です。
- `OpenWakeWordMelspectrogramModelPath`: OpenWakeWord の特徴量生成に使う `melspectrogram.onnx` です。
- `OpenWakeWordEmbeddingModelPath`: OpenWakeWord の特徴量生成に使う `embedding_model.onnx` です。
- `OpenWakeWordThreshold`: 反応のしきい値です。誤反応が多い場合は上げ、反応しにくい場合は下げます。
- `OpenWakeWordDevice`: マイクを明示指定したい場合にデバイス番号を入れます。
- `UseNewChatGpt`: `true`（既定）で新版の通常チャットを毎回新規に開きます。以下の Classic 用検出・位置クリック設定は `false` のときだけ使います。
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

## About / ライセンス

タスクトレイメニューの `About / ライセンス` から、アプリのバージョンと利用している OSS ライブラリのライセンス情報を確認できます。

依存パッケージを追加、削除、更新した場合は次の手順でライセンス情報も更新してください。

1. `.\setup_openwakeword.ps1` または `.\build.ps1` を実行して NuGet パッケージを復元します。
2. `src\VoiceChatLauncher\VoiceChatLauncher.Dependencies.csproj`、`build.ps1` のコピー対象 DLL、`packages\*\*\*.nuspec` の `id` / `version` / `license` / `projectUrl` を確認します。
3. About 画面の表示元である `src\VoiceChatLauncher\ThirdPartyLicenses.cs` を実際の依存関係に合わせて更新します。
4. バージョンを変更する場合は `src\VoiceChatLauncher\Properties\AssemblyInfo.cs` の `AssemblyVersion`、`AssemblyFileVersion`、`AssemblyInformationalVersion` を更新します。
5. `.\build.ps1` でビルドできることを確認します。

## OpenWakeWord

OpenWakeWord は C# runtime で動作します。Python や `.venv` は不要です。

初回セットアップで、C# runtime が特徴量生成に使う `melspectrogram.onnx` と `embedding_model.onnx` を `models` フォルダにダウンロードします。

このリポジトリでは `models\Hey_Lucy_20260609_095011.onnx` を使う設定になっています。

### WakeWord を変更する

OpenWakeWord 用に作成された `.onnx` ファイルを `models` フォルダに置きます。

```text
models\my_wakeword.onnx
```

その後、設定画面の `OpenWakeWord` タブで `モデル` を変更します。`bin\config.ini` を直接編集する場合は次のように指定します。

```ini
OpenWakeWordModels=..\models\my_wakeword.onnx
```

`VoiceChatLauncher.exe` は `bin` フォルダから動くため、プロジェクト直下の `models` を指すには `..\models\...` と指定します。絶対パスも使えます。

```ini
OpenWakeWordModels=C:\path\to\my_wakeword.onnx
```

複数モデルを同時に待ち受ける場合は `|` で区切ります。

```ini
OpenWakeWordModels=..\models\my_wakeword.onnx|..\models\other_wakeword.onnx
```

### 反応しやすさを調整する

```ini
OpenWakeWordThreshold=0.80
```

- 反応しにくい場合は `0.70` などへ下げます。
- 誤反応が多い場合は `0.90` などへ上げます。

直接テストしたい場合は、設定画面で `スコアをログ出力する` を有効にして保存し、`bin\voice-command.log` の `SCORE` と `WAKE` 行を確認してください。

`OpenWakeWordDevice` は Windows の waveIn デバイス番号です。空なら既定のマイクを使います。

## 注意

- ChatGPT アプリにログイン済みで、マイク権限が許可されている必要があります。
- Windows の「デスクトップ アプリがマイクにアクセスできるようにする」がオフだと待ち受けできません。
- Windows の音声認識と言語パックは不要です。
- C# runtime では `OpenWakeWordVadThreshold` はまだ使用されません。
- ChatGPT アプリの画面構造が変わるとボタン検出に失敗する場合があります。その場合は `bin\voice-command.log` を見て、`VoiceButtonNames` や `WindowTitleKeyword` を調整してください。
