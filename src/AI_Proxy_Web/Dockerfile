FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
WORKDIR /App
COPY AI_Proxy_Web.csproj ./
# Restore as distinct layers
RUN dotnet restore  --disable-parallel

COPY . ./
RUN dotnet publish --property WarningLevel=0 -c Release -o out
COPY config.json out/


FROM mcr.microsoft.com/dotnet/aspnet:8.0
ENV TZ=Asia/Shanghai
# Suppress an apt-key warning about standard out not being a terminal. Use in this script is safe.
ENV APT_KEY_DONT_WARN_ON_DANGEROUS_USAGE=DontWarn
# export DEBIAN_FRONTEND="noninteractive"
ENV DEBIAN_FRONTEND noninteractive
    
WORKDIR /App
RUN sed -i 's@deb.debian.org@mirrors.tuna.tsinghua.edu.cn@g' /etc/apt/sources.list.d/debian.sources
RUN apt-get -y update && apt-get -y upgrade && apt-get install -y --no-install-recommends wget unzip \
        apt-transport-https \
    	ca-certificates \
    	curl \
    	gnupg \
        && curl -sSL https://dl.google.com/linux/linux_signing_key.pub | apt-key add - \
    	&& echo "deb [arch=amd64] https://dl.google.com/linux/chrome/deb/ stable main" > /etc/apt/sources.list.d/google-chrome.list \
    	&& apt-get update && apt-get install -y \
    	google-chrome-stable ffmpeg \
    	--no-install-recommends \
    	&& apt-get purge --auto-remove -y curl gnupg \
    	&& rm -rf /var/lib/apt/lists/*
    
COPY chromedriver_linux64.zip .
RUN unzip chromedriver_linux64.zip && \
    mv chromedriver /usr/bin && rm -f chromedriver_linux64.zip
    
RUN apt-get -y update && apt-get install -y --no-install-recommends fonts-noto-cjk npm \
    	&& rm -rf /var/lib/apt/lists/*

RUN yes | npx playwright@1.51.0 install chromium && npm cache clean --force

COPY fonts/* /usr/share/fonts
RUN fc-cache -fv

COPY --from=build-env /App/out .
ENTRYPOINT ["dotnet", "AI_Proxy_Web.dll"]
