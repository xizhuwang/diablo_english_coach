param([string]$PackageRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$runtime = Join-Path ([IO.Path]::GetFullPath($PackageRoot)) 'translation-runtime'
New-Item -ItemType Directory -Force $runtime | Out-Null
function Fetch([string]$Url, [string]$Hash, [string]$Name) {
    $path = Join-Path $runtime $Name
    if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $Hash) {
        Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $path
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $Hash) { throw "下載驗證失敗：$Name" }
    return $path
}
$python = Fetch 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip' '4ACBED6DD1C744B0376E3B1CF57CE906F9DC9E95E68824584C8099A63025A3C3' 'python.zip'
Expand-Archive -LiteralPath $python -DestinationPath (Join-Path $runtime 'python') -Force
$wheels = @(
    @('https://files.pythonhosted.org/packages/4e/23/e3b5322ff7368fcbed181ea4c209149416e7940b5b04971d5ee4084afe1a/ctranslate2-4.8.2-cp312-cp312-win_amd64.whl','d94421d565d0de61c032998f737a18942b0f2bef40c0424b1846ec6f67300105'),
    @('https://files.pythonhosted.org/packages/8d/11/753fca2e6b109be3ab7867abf357dfe48677fe726ae5a5363d0b54ca9450/sentencepiece-0.2.2-cp312-cp312-win_amd64.whl','7c6e7bf684dc12145bfa685d3060beaea55139134ba848289bee514ed42e7383'),
    @('https://files.pythonhosted.org/packages/7f/b9/87fea2769fe1c47c1b5b01d8310772c9d1a85d485de7cf386ef7a3332b02/numpy-2.5.2-cp312-cp312-win_amd64.whl','28ac63476ec7651484215ee7fa15a1f78b57c14621f01e392afe17b9a1390ce4'),
    @('https://files.pythonhosted.org/packages/86/bf/899e81e4cce32febab4fb42bb97dcdf66bc135272882d1987881a4b519e9/pyyaml-6.0.3-cp312-cp312-win_amd64.whl','5fcd34e47f6e0b794d17de1b4ff496c00986e1c83f7ab2fb8fcfe9616ff7477b'),
    @('https://files.pythonhosted.org/packages/30/6b/055b7806f320cc8f2cdf23c5f70221c0dc1683fca9ffaf76dfc2ad4b91b6/opencc_python_reimplemented-0.1.7-py2.py3-none-any.whl','41b3b92943c7bed291f448e9c7fad4b577c8c2eae30fcfe5a74edf8818493aa6')
)
$i = 0
foreach ($wheel in $wheels) {
    $archive = Fetch $wheel[0] $wheel[1] "dependency-$i.zip"
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $runtime 'packages') -Force
    $i++
}
$model = Fetch 'https://argos-net.com/v1/translate-en_zh-1_9.argosmodel' '433E7C4F034D87FBE2353161E05F18646D7999452F801A4E1F0378522B9850AB' 'model.zip'
Expand-Archive -LiteralPath $model -DestinationPath (Join-Path $runtime 'model') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'translation-worker.py') -Destination $runtime -Force
$worker = Join-Path $runtime 'translation-worker.py'
$exe = Join-Path $runtime 'python\python.exe'
$reply = '{"id":1,"text":"The door remains closed.","threads":1}' | & $exe -u $worker
if ($LASTEXITCODE -ne 0 -or -not (($reply | ConvertFrom-Json).text)) { throw '地端翻譯安裝測試失敗' }
Write-Host 'CPU 地端翻譯已安裝並通過測試。' -ForegroundColor Green
