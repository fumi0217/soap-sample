# SOAP MTOM sample (C# / .NET 8)

C# で SOAP **MTOM**（Message Transmission Optimization Mechanism）リクエストを送るクライアントと、それを受ける最小限のモックサーバーのペアです。

## この構成に至った経緯

「安全に投げられる公開の SOAP+MTOM テストエンドポイント」を探しましたが、見つかった候補はいずれも2004〜2005年頃の SOAP/MTOM 相互運用性テストで使われていた個人・研究用ドメインで、現在は名前解決すらできませんでした。今も生きている実用的な公開 MTOM エンドポイントは実質存在しません。

その後、実在する特定サービス（企業の文書管理システムのWSDL）への接続も検討しましたが、以下の理由から**そのサービスへの実接続はせず**、一般的な設計パターンだけを参考にしたオリジナルのモックサービスを自作する方針にしました。

- 対象システムはネットワークレベルで制限されており（WAFで`403 Access Denied`）、そもそもこの環境から到達不可
- 正規のログイン認証情報を持っていない

このリポジトリの `MockDocSuiteService` は、**実在の企業・製品とは無関係な、完全にオリジナルの名称・namespace・挙動を持つ架空のサービス**です。「セッションベースのログイン」「I18nString的な多言語ラベル型」「オブジェクト検索」「バイナリ添付付きのオブジェクトアップロード」という、企業向け文書管理/ワークフロー系SOAPサービスによくある一般的な設計パターンだけを参考にしています。内部処理はすべてモック（インメモリの偽データ）です。

## 構成

```
src/
  MtomI18nService/   ASP.NET Core の最小サーバー（SOAP+MTOM を手動でパース/生成、MockDocSuiteを実装）
  MtomI18nClient/     WCF クライアント（BasicHttpBinding + MessageEncoding.Mtom）
```

`IMockDocSuiteService` が公開する操作（すべて `/MockDocSuite.svc` への POST。ボディのルート要素名で振り分け）：

| 操作 | 内容 |
|---|---|
| `Login` | `UserId`/`Credential` を受け取り `SessionId` を発行（モック：値があれば常に成功） |
| `SearchRepositoryObjects` | `SessionId`/`Query` を受け取り、`I18nString`（多言語ラベル）付きの固定サンプル結果を返す |
| `UploadRepositoryObject` | `SessionId` + `I18nString`ラベル + ファイル名 + **バイナリ添付**を送信。**MTOMが効くのはここ** |
| `Logout` | `SessionId` を無効化 |

`I18nString` は `List<I18nLabel>`（`{Lang, Value}` の組）として表現しており、`UploadRepositoryObject` では実際に日/仏/独/韓/中の5言語ラベルを送信します。`Attachment`(`Content`)は MTOM 有効時、WCF が base64 に膨らませてインライン化せず `<xop:Include>` で参照する生の MIME パートとして送信します。

### なぜ CoreWCF ではなく手書きサーバーなのか

サーバー側は CoreWCF ではなく素の ASP.NET Core（`MimeKit` で multipart/related をパース）で実装しています。CoreWCF は現時点でサーバー側の MTOM メッセージエンコーダーを実装していません（[CoreWCF/CoreWCF#10](https://github.com/CoreWCF/CoreWCF/issues/10)）。一方クライアント側の `System.ServiceModel.Http`（.NET 用 WCF クライアントパッケージ）は MTOM 送信に対応しているため、クライアントは正規の WCF スタックを使い、サーバーだけプロトコルを直接ハンドリングしています。

## ビルド・実行方法

.NET 8 SDK が必要です。

```bash
dotnet build

# ターミナル1: サーバー起動 (http://localhost:5205)
dotnet run --project src/MtomI18nService

# ターミナル2: クライアント実行 (Login -> Search -> Upload(MTOM) -> Logout)
dotnet run --project src/MtomI18nClient
# 別エンドポイントに向けたい場合:
dotnet run --project src/MtomI18nClient -- http://example.com/MockDocSuite.svc
```

### 実行結果例

```
Endpoint     : http://localhost:5205/MockDocSuite.svc
Message enc. : Mtom (MTOM)

--- Login ---
Success   : True
SessionId : a924048b54ed46e3baf2a8b0b629341d
Message   : Mock login OK for 'demo-user'.

--- SearchRepositoryObjects ---
  [obj-1001] ja=「quarterly-report」検索結果サンプル 1, en=Sample search result 1 for 'quarterly-report' (modified 2026-08-01)
  [obj-1002] ja=四半期レポート（サンプル）, en=Quarterly report (sample) (modified 2026-07-15)

--- UploadRepositoryObject (MTOM) ---
Label (i18n) : [ja] ご注文ありがとうございます / [fr] Merci beaucoup / [de] Danke schön / [ko] 감사합니다 / [zh] 谢谢惠顾
Attachment   : sample.bin (6144 bytes)
ObjectId  : obj-249b483f
SizeBytes : 6144
Message   : Stored (mock) 6144 byte(s) as 'sample.bin' with 5 label(s).

--- Logout ---
Message   : Session a924048b54ed46e3baf2a8b0b629341d closed (mock).
```

## ワイヤ検証済み

実際の HTTP リクエストのバイト列をキャプチャして、正しく MTOM/XOP になっていることを確認済みです：

```
Content-Type: multipart/related; type="application/xop+xml"; start="<http://tempuri.org/0>"; boundary="uuid:...+id=1"; start-info="text/xml"

--uuid:...+id=1
Content-Type: application/xop+xml;charset=utf-8;type="text/xml"
<s:Envelope ...><UploadRepositoryObjectRequest ...>
  <Label>
    <I18nLabel><Lang>ja</Lang><Value>ご注文ありがとうございます</Value></I18nLabel>
    <I18nLabel><Lang>fr</Lang><Value>Merci beaucoup</Value></I18nLabel>
    ...
  </Label>
  <Content><xop:Include href="cid:http://tempuri.org/1/..." .../></Content>
</UploadRepositoryObjectRequest>...

--uuid:...+id=1
Content-Type: application/octet-stream
Content-Transfer-Encoding: binary
<バイナリがそのまま(base64化されずに)ここに入る>
```

i18n文字列は SOAP 本文中に UTF-8 のテキストとしてそのまま流れ、添付バイナリは base64 化されず別 MIME パートとして直接転送されています。これが MTOM の最適化そのものです（テキストのみに MTOM を使っても得られる恩恵はなく、恩恵はバイナリ添付の部分に出ます）。サーバーのログでも、送信した5件のラベルが正しく `with 5 label(s)` としてパースできていることが確認できています。

## 本番投入する場合の注意

このサンプルは学習・検証目的の最小構成です。実運用する場合は最低限:

- `BasicHttpSecurityMode.None` → `Transport`（HTTPS必須）に変更する
- `Login` を本物の認証（パスワードハッシュ検証・トークン発行など）に置き換える
- サーバー側で `Content-Length` / パート数の上限チェックを入れ、巨大な multipart で DoS されないようにする
- 添付ファイルの拡張子・内容検証（ファイルアップロードとして扱うなら）
- SOAP Fault のステータスコード・詳細度（情報漏えいにならないか）を見直す
