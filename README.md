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
