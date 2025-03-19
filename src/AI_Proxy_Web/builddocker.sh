dotnet restore  --disable-parallel
dotnet publish --property WarningLevel=0 -c Release -o bin/out
docker build --add-host dl.google.com:220.181.174.225 -t ai_proxy .