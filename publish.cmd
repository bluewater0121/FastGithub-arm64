set output=./publish
if exist "%output%" rd /S /Q "%output%"
dotnet publish -c Release /p:PublishSingleFile=true --self-contained -r win-arm64 -o "%output%/fastgithub_win-arm64" ./FastGithub/FastGithub.csproj
