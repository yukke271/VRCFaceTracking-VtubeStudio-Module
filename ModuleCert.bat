@echo off
chcp 932 >nul
setlocal

echo �t�@�C���̃n�b�V���l�����؂��܂�
echo ���W���[���̃o�[�W�����́u0.5.0�v�ł�

:: ���؂���n�b�V���l�̓o�[�W�����X�V���ɓs�x�ύX����
set "filePath=VtubeStadioExtTrackingInterface.zip"
set "expectedHash=68c9944320008e46af13bdcc89537fa3f1e843ac632ef4ac659620746b4aefd8"

:: CertUtil�R�}���h��SHA256�n�b�V���l���v�Z
for /f "skip=1 tokens=1" %%A in ('certutil -hashfile "%filePath%" SHA256') do (
    set "actualHash=%%A"
    goto compare
)

:compare
:: �n�b�V���l��\��
echo �t�@�C��: %filePath%
echo �����l: %actualHash%
echo ���Ғl: %expectedHash%

:: �n�b�V���l���r
if /i "%actualHash%"=="%expectedHash%" (
    echo ����l�ł��B
) else (
    echo �ُ�l�ł��B���̃t�@�C���̎g�p�͍T���A�J���҂ɕ񍐂��Ă��������B
)

endlocal
pause
