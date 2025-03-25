#1. 安装rustup
#curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
#2. M芯片的Mac上要安装x64的包
#rustup target add x86_64-apple-darwin
#3. 然后才能运行以下脚本编译Mac Universal的包
#获取以下签名信息需要Apple开发者账号，流程参考https://www.w3cschool.cn/tauri/tauri-signs-tauri-application.html
export APPLE_SIGNING_IDENTITY="xx"
export APPLE_CERTIFICATE="xx"
export APPLE_CERTIFICATE_PASSWORD="xx"
export APPLE_ID="xx"
export APPLE_PASSWORD="xx"
export APPLE_TEAM_ID="xx"
export APPLE_API_KEY_PATH="./libs/xx.p8"
export APPLE_API_KEY="xx"
export APPLE_API_ISSUER="xx"
npm run tauri build