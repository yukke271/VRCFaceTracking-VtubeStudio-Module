@echo off
chcp 932 >nul
setlocal

echo ファイルのハッシュ値を検証します
echo モジュールのバージョンは「0.5.0」です

:: 検証するハッシュ値はバージョン更新時に都度変更する
set "filePath=VtubeStadioExtTrackingInterface.zip"
set "expectedHash=68c9944320008e46af13bdcc89537fa3f1e843ac632ef4ac659620746b4aefd8"

:: CertUtilコマンドでSHA256ハッシュ値を計算
for /f "skip=1 tokens=1" %%A in ('certutil -hashfile "%filePath%" SHA256') do (
    set "actualHash=%%A"
    goto compare
)

:compare
:: ハッシュ値を表示
echo ファイル: %filePath%
echo 実測値: %actualHash%
echo 期待値: %expectedHash%

:: ハッシュ値を比較
if /i "%actualHash%"=="%expectedHash%" (
    echo 正常値です。
) else (
    echo 異常値です。このファイルの使用は控え、開発者に報告してください。
)

endlocal
pause
