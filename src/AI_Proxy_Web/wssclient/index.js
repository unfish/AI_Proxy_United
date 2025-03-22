import * as Lark from '@larksuiteoapi/node-sdk';
import { readFile } from 'fs/promises';
import path from 'path';
import axios from 'axios';

const main = async () => {
  try {
    // 读取上一级目录的 config.json 文件
    const configPath = path.join(process.cwd(), 'config.json');
    const configData = await readFile(configPath, 'utf8');
    const config = JSON.parse(configData);
    const AppId = config.FeiShu.Main.AppId;
    const AppSecret = config.FeiShu.Main.AppSecret;
    const postUrl = "http://localhost:8080/api/ai/feishu/event"

    const baseConfig = {
      appId: AppId,
      appSecret: AppSecret,
      domain: "https://open.feishu.cn",
    };
    const wsClient = new Lark.WSClient(baseConfig);
    const eventDispatcher = new Lark.EventDispatcher({}).register({
      'im.message.receive_v1': async (data) => {
        const d = {
          header:{event_type: data.event_type},
          event:data
        }
        console.log('Got message: '+data.event_type+' '+(new Date()).toLocaleString());
        await axios.post(postUrl, d, {
          headers: {
              'Content-Type': 'application/json'
          }
        });
      },
      'im.chat.access_event.bot_p2p_chat_entered_v1': async (data) => {
        const {
          operator_id: { open_id },
        } = data;
      },
      'application.bot.menu_v6': async (data) => {
        const d = {
          header:{event_type: data.event_type},
          event:data
        }
        console.log('Got message: '+data.event_type+' '+(new Date()).toLocaleString());
        await axios.post(postUrl, d, {
          headers: {
              'Content-Type': 'application/json'
          }
        });
      },
      'card.action.trigger': async (data) => {
        const d = {
          header:{event_type: data.event_type},
          event:data
        }
        console.log('Got message: '+data.event_type+' '+(new Date()).toLocaleString());
        const response = await axios.post(postUrl, d, {
          headers: {
              'Content-Type': 'application/json'
          }
        });
        return response.data;
      }
    });

    wsClient.start({ eventDispatcher });
  } catch (parseErr) {
    console.error('解析配置文件时出错:', parseErr);
  }
}

// 调用主函数
main();