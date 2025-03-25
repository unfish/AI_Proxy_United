<template>
  <main class="container">
    <template v-if="hasToken">
      <a-space direction="vertical" fill :style="'min-height:'+chatListMinHeight+'px;'">
        <Markdown v-if="showMonitorSizeTips && chatAnswers.length===0" :md-id="'A-Tips'" class="tips" :content="monitorSizeTips"></Markdown>
        <template v-for="(item, index) of chatAnswers">
          <Markdown v-if="item.type === 'assistant' || item.type === 'user'" :md-id="'A' + index"
            :class="item.type" :content="item.content"></Markdown>
          <template v-else>
            <div :class="item.type" v-if="item.content.length > 0">
              <div class="content">{{ item.content }}</div>
            </div>
          </template>
        </template>
      </a-space>
      <a-space direction="vertical" fill ref="divCounter" class="input">
        <a-select v-model="selectedMonitorId" placeholder="Select a monitor"
          v-if="monitors.length > 1" @change="selectedMonitorChanged">
          <a-option v-for="item of monitors" :key="item.id" :value="item.id">{{ item.name }}</a-option>
        </a-select>
        <a-textarea placeholder="请输入您的指令" v-model="prompt" :auto-size="{ minRows: 4, maxRows: 6 }" />
        <a-space fill class="buttons">
          <a-popover v-if="autoWorkMode && !useCutMode && showMonitorSizeTips">
            <a-button type="primary" :disabled="true" shape="round">
              <template #icon>
                <icon-send />
              </template>
              <template #default>发送</template>
            </a-button>
            <template #content>
              <p>请调低分辨率或切换到裁剪模式后使用。</p>
              <p>注意：调整分辨率后需要重启应用。</p>
            </template>
          </a-popover>
          <a-button type="primary" @click="startChat('button')" :loading="processingChatGpt" shape="round" v-else>
            <template #icon>
              <icon-send />
            </template>
            <template #default>发送</template>
          </a-button>
          <a-button shape="round" size="mini" @click="clearChatContexts" :disabled="processingChatGpt || chatAnswers.length === 0">
            <template #icon>
              <icon-user-add />
            </template>
            <template #default>新会话</template>
          </a-button>
          <a-popover>
            <a-switch v-model="autoWorkMode" @change="autoWorkModeChange" :disabled="processingChatGpt">
              <template #checked>
                自动化
              </template>
              <template #unchecked>
                仅聊天
              </template>
            </a-switch>
            <template #content>
              <p>仅聊天状态下将关闭大模型的任务分解与自动控制功能，仅用于聊天问答。</p>
              <p>可以避免普通问题被作为电脑操作任务来处理，影响速度与准确性。</p>
            </template>
          </a-popover>
          <a-popover>
            <a-switch v-model="useCutMode" @change="useCutModeChange" :disabled="processingChatGpt || !autoWorkMode">
              <template #checked>
                裁剪
              </template>
              <template #unchecked>
                压缩
              </template>
            </a-switch>
            <template #content>
              <p>使用压缩模式时会将全屏截图压缩到要求的大小，如果当前分辨率过高会造成压缩后的图片质量很低文字很小，AI识别困难，请先调小屏幕分辨率再使用。</p>
              <p>
                使用裁剪模式时全屏截图后不压缩，只裁剪左上角1280x800的屏幕范围发送给AI，如果当前任务不需要AI在全屏范围查找操作按钮或软件，比如只需要在浏览器内就可以完成操作，可以使用该功能。请先将浏览器调整到屏幕左上角1280x800的范围内，可以使用系统截图功能确认尺寸。
              </p>
              <p>点击该按钮会将本程序窗口左上角移到1280x800的位置，请确保AI要使用的程序窗口宽高都不超出本程序左上角的位置。<a-button
                  @click="movePositionTo1280">参考位置</a-button></p>
            </template>
          </a-popover>
          <a-dropdown position="tr" @select="handleMenu">
            <a-button type="secondary" shape="circle">
              <template #icon>
                <icon-settings />
              </template>
            </a-button>
            <template #content>
              <a-doption value="test">
                <template #icon>
                  <icon-robot />
                </template>
                <template #default>功能测试</template>
              </a-doption>
              <a-doption value="tasks">
                <template #icon>
                  <icon-select-all />
                </template>
                <template #default>批量任务</template>
              </a-doption>
              <a-dsubmenu value="export" trigger="hover">
                <template #default>导出对话</template>
                <template #content>
                  <a-doption value="export-txt">保存为TXT</a-doption>
                </template>
              </a-dsubmenu>
              <a-dsubmenu value="samples" trigger="hover">
                <template #default>功能示例</template>
                <template #content>
                  <a-doption value="sample-web">网页浏览</a-doption>
                </template>
              </a-dsubmenu>
            </template>
          </a-dropdown>
        </a-space>
      </a-space>
    </template>
    <template v-else>
      <a-space direction="vertical" fill style="min-height:600px;text-align:center;" class="login">
        <div>请先配置src/libs/util.js文件中的服务器端HostUrl和MasterToken。</div>
      </a-space>
    </template>
  </main>
</template>

<script setup lang="ts">
import { ref, nextTick, useTemplateRef, onMounted, onUnmounted } from "vue";
import { fetch } from '@tauri-apps/plugin-http';
import {
  getCursorPosition,
  getMonitors,
  mouseClick,
  mouseDoubleClick,
  mouseDrag,
  moveMouse,
  pressKey,
  takeScreenshot,
  typeText,
  Monitor,
  justWait,
  scrollMouse,
  holdKey,
  mouseDown,
  mouseUp
} from "../libs/computer";
import { Command } from '@tauri-apps/plugin-shell';
import { exists, writeTextFile, readTextFile } from '@tauri-apps/plugin-fs';
import { download } from '@tauri-apps/plugin-upload';
import { BaseDirectory } from '@tauri-apps/api/path';
import { save, open } from '@tauri-apps/plugin-dialog';
import { WebviewWindow } from '@tauri-apps/api/webviewWindow';
import { getCurrentWindow, LogicalPosition } from '@tauri-apps/api/window';
import { emit, listen } from '@tauri-apps/api/event';
import { scaleCoordinates, ScalingSource } from "../libs/scaling";
import { mainPrompt } from "../libs/prompts";
import { askGpt, setToolResult } from "../libs/util.gpt";
import Markdown from '../components/markdown/index.vue';
import { Message } from '@arco-design/web-vue';
import QrcodeVue from 'qrcode.vue';
import global from "../libs/util";

type Chat = {
  type: string;
  content: string;
};
const divCounter = useTemplateRef('divCounter')
const chatListMinHeight = ref(500);
const selectedMonitorId = ref("");
const autoWorkMode = ref(true); // 自动化还是仅聊天
const useCutMode = ref(false); //分辨率大于标准分辨率的时候，是否使用裁剪模式
const monitors = ref<Monitor[]>([]);

const prompt = ref("");
const chatAnswers = ref<Chat[]>([]);
var processingChatGpt = ref(false);
var currentLogId = 0;
var currentSessionId = '';
const showMonitorSizeTips = ref(false);
const monitorSizeTips = ref("");

function clearChatContexts(){
  chatAnswers.value = [];
  currentSessionId = '';
  currentLogId = 0;
  showMonitorSizeTips.value = false;
}

//开始任务，可以从按钮或者批量任务触发
async function startChat(from: string) {
  if (processingChatGpt.value) {
    Message.error('任务已经在运行中!')
    return;
  }
  if (prompt.value.length < 2) {
    Message.error('请输入任务的详细描述!')
    return;
  }
  processingChatGpt.value = true;

  const selectedMonitor = monitors.value.find((m) => m.id === selectedMonitorId.value);
  //console.log("selectedMonitor", selectedMonitor);
  if (chatAnswers.value.length === 0 && from === 'button') {
    currentTaskIndex = -1;
    currentAutoNext = false;
  }
  chatAnswers.value.push({ content: prompt.value, type: 'user' })
  var frontFuncs: { name: string; args: string; id: string; }[] = [];
  var chatLength = chatAnswers.value.length;
  let chatModel = autoWorkMode.value? global.agentModel : global.chatModel;
  let promptText = autoWorkMode.value && chatLength===0 ? (mainPrompt + '\n\nThis is what you need to do:\n' + prompt.value) : prompt.value;
  prompt.value = ''
  askGpt({ prompt: promptText, chatModel: chatModel, withContext: chatLength > 1, displayWidth: selectedMonitor?.width ?? 1920, displayHeight: selectedMonitor?.height ?? 1080 },
    function (str, type) {
      if (chatAnswers.value[chatAnswers.value.length - 1].type !== 'assistant' && (type === 'Answer' || type === 'Error')) {
        chatAnswers.value.push({ content: '', type: 'assistant' })
      }
      if (type === 'Answer' || type === 'Error') {
        chatAnswers.value[chatAnswers.value.length - 1].content += str
        divCounter.value.$el.scrollIntoView({ behavior: 'smooth' });
      }
    },
    async function (result, logId, sessionId) {
      processingChatGpt.value = false;
      currentLogId = logId;
      currentSessionId = sessionId;
      divCounter.value.$el.scrollIntoView({ behavior: 'smooth' });
      if (frontFuncs.length > 0) {
        await processFunctions(frontFuncs);
      } else {
        await sendResultToTasksWindow(result);
      }
    },
    function (error) {
      chatAnswers.value.push({ content: error, type: 'error' })
      processingChatGpt.value = false;
    },
    function (name: string, args: string, id: string) {
      frontFuncs.push({ name, args, id })
      chatAnswers.value.push({ content: args.length > 50 ? args.substring(0, 50) + '...' : args, type: 'action' })
    }
  );
}

//当前一批鼠标键盘动作执行完成后，将结果重新提交到AI，继续任务
async function returnActionResult(result) {
  var frontFuncs: { name: string; args: string; id: string; }[] = [];
  processingChatGpt.value = true;
  setToolResult(result,
    function (str, type) {
      if (chatAnswers.value[chatAnswers.value.length - 1].type !== 'assistant' && (type === 'Answer' || type === 'Error')) {
        chatAnswers.value.push({ content: '', type: 'assistant' })
      }
      if (type === 'Answer' || type === 'Error') {
        chatAnswers.value[chatAnswers.value.length - 1].content += str
        divCounter.value.$el.scrollIntoView({ behavior: 'smooth' });
      }
    },
    async function (result) {
      processingChatGpt.value = false;
      prompt.value = ''
      divCounter.value.$el.scrollIntoView({ behavior: 'smooth' });
      if (frontFuncs.length > 0) {
        await processFunctions(frontFuncs)
      } else {
        await sendResultToTasksWindow(result);
      }
    },
    function (error) {
      chatAnswers.value.push({ content: error, type: 'error' })
      processingChatGpt.value = false;
    },
    function (name: string, args: string, id: string) {
      frontFuncs.push({ name, args, id })
      chatAnswers.value.push({ content: args.length > 50 ? args.substring(0, 50) + '...' : args, type: 'action' })
    }
  );
}

//任务执行结束，将结果返回给批量任务窗口
async function sendResultToTasksWindow(result) {
  if (currentTaskIndex > -1) {
    await emit('to-tasks', { type: 'task-result', message: { result, index: currentTaskIndex, autoNext: currentAutoNext } });
  }
}

var lastFunctionTime = new Date();
//处理当前一批鼠标键盘动作
async function processFunctions(funcs: { name: string; args: string; id: string; }[]) {
  var result = []
  for (var i in funcs) {
    var func = funcs[i]
    var t = await processFunction(func.name, func.args, func.id)
    lastFunctionTime = new Date();
    if (t) {
      result.push(t)
    }
  }
  await returnActionResult(result);
}

function getResult(id, type, content, mimeType) {
  return { tool_id: id, result_type: type, content: content, mime_type: mimeType }
}

//具体的单个动作执行方法
async function processFunction(name, args, id) {
  if (name === 'computer') {
    return await processComputerFunction(name, args, id);
  } else if (name === 'bash') {
    return await processBashFunction(name, args, id);
  } else if (name === 'str_replace_editor') {
    return await processFileFunction(name, args, id);
  }
}

//鼠标键盘类处理函数
async function processComputerFunction(name, args, id) {
  const selectedMonitor = monitors.value.find((m) => m.id === selectedMonitorId.value);
  //console.log("selectedMonitor", selectedMonitor);

  var o = JSON.parse(args)
  var action = o.action
  if (action === 'screenshot') {
    var curTime = new Date();
    if (curTime.getTime() - lastFunctionTime.getTime() < 1000) {
      await new Promise(resolve => setTimeout(resolve, 2000)); //如果前面刚刚执行过其它操作，等待2秒
    }
    const scaledDimensions = scaleCoordinates({
      source: ScalingSource.COMPUTER,
      screenDimensions: {
        width: selectedMonitor?.width ?? 1920,
        height: selectedMonitor?.height ?? 1080,
      },
      x: selectedMonitor?.width ?? 1920,
      y: selectedMonitor?.height ?? 1080,
      useCutMode: useCutMode.value
    });
    const screenshot = await takeScreenshot({
      monitorId: selectedMonitorId.value,
      resizeX: scaledDimensions[0],
      resizeY: scaledDimensions[1],
      useCutMode: useCutMode.value,
      scaleFactor: selectedMonitor?.scale_factor ?? 1,
    });
    console.log("screenshot taken");
    return getResult(id, "image", screenshot, "png")
  } else if (action === "mouse_move" && o.coordinate) {
    const scaledCoordinates = scaleCoordinates({
      source: ScalingSource.API,
      screenDimensions: {
        width: selectedMonitor?.width ?? 1920,
        height: selectedMonitor?.height ?? 1080,
      },
      x: o.coordinate[0],
      y: o.coordinate[1],
      useCutMode: useCutMode.value
    });

    await moveMouse(
      selectedMonitorId.value,
      scaledCoordinates[0],
      scaledCoordinates[1]
    );
    console.log("moved mouse to", scaledCoordinates[0], scaledCoordinates[1]);
    return getResult(id, "text", "moved mouse to " + scaledCoordinates[0] + "," + scaledCoordinates[1], "text/plain")
  } else if (action === "left_click") {
    if (o.coordinate) {
      const scaledCoordinates = scaleCoordinates({
        source: ScalingSource.API,
        screenDimensions: {
          width: selectedMonitor?.width ?? 1920,
          height: selectedMonitor?.height ?? 1080,
        },
        x: o.coordinate[0],
        y: o.coordinate[1],
        useCutMode: useCutMode.value
      });

      await mouseClick(
        selectedMonitorId.value,
        "left",
        scaledCoordinates[0],
        scaledCoordinates[1]
      );
    } else {
      await mouseClick(selectedMonitorId.value, "left");
    }
    console.log("mouse clicked");
    return getResult(id, "text", "clicked mouse", "text/plain")
  } else if (action === 'right_click') {
    if (o.coordinate) {
      const scaledCoordinates = scaleCoordinates({
        source: ScalingSource.API,
        screenDimensions: {
          width: selectedMonitor?.width ?? 1920,
          height: selectedMonitor?.height ?? 1080,
        },
        x: o.coordinate[0],
        y: o.coordinate[1],
        useCutMode: useCutMode.value
      });

      await mouseClick(
        selectedMonitorId.value,
        "right",
        scaledCoordinates[0],
        scaledCoordinates[1]
      );
    } else {
      await mouseClick(selectedMonitorId.value, "right");
    }
    return getResult(id, "text", "clicked mouse", "text/plain")
  } else if (action === "double_click") {
    if (o.coordinate) {
      const scaledCoordinates = scaleCoordinates({
        source: ScalingSource.API,
        screenDimensions: {
          width: selectedMonitor?.width ?? 1920,
          height: selectedMonitor?.height ?? 1080,
        },
        x: o.coordinate[0],
        y: o.coordinate[1],
        useCutMode: useCutMode.value
      });

      await mouseDoubleClick(
        selectedMonitorId.value,
        "left",
        scaledCoordinates[0],
        scaledCoordinates[1]
      );
    } else {
      await mouseDoubleClick(selectedMonitorId.value, "left");
    }
    console.log("mouse double clicked");
    return getResult(id, "text", "clicked mouse", "text/plain")
  } else if (action === "left_click_drag" && o.coordinate) {
    const scaledCoordinates = scaleCoordinates({
      source: ScalingSource.API,
      screenDimensions: {
        width: selectedMonitor?.width ?? 1920,
        height: selectedMonitor?.height ?? 1080,
      },
      x: o.coordinate[0],
      y: o.coordinate[1],
      useCutMode: useCutMode.value
    });

    await mouseDrag(
      selectedMonitorId.value,
      scaledCoordinates[0],
      scaledCoordinates[1]
    );
    console.log("draged mouse to", scaledCoordinates[0], scaledCoordinates[1]);
    return getResult(id, "text", "draged mouse to " + scaledCoordinates[0] + "," + scaledCoordinates[1], "text/plain")
  } else if (action === 'cursor_position') {
    const position = await getCursorPosition(selectedMonitorId.value);
    return getResult(id, "text", 'cursor position: [' + position.x + ', ' + position.y + ']', "text/plain")
  } else if (action === 'type' && o.text) {
    await typeText(o.text);
    return getResult(id, "text", "typed text", "text/plain")
  } else if (action === 'key' && o.text) {
    await pressKey(o.text);
    return getResult(id, "text", "pressed key", "text/plain")
  } else if (action === 'wait' && o.duration) {
    await justWait(o.duration);
    return getResult(id, "text", "waited for " + o.duration + " seconds", "text/plain")
  } else if (action === 'scroll' && o.scroll_amount && o.scroll_direction) {
    await scrollMouse(o.scroll_amount, o.scroll_direction);
    return getResult(id, "text", "waited for " + o.duration + " seconds", "text/plain")
  } else if (action === 'hold_key' && o.text && o.duration) {
    await holdKey(o.text, o.duration);
    return getResult(id, "text", "holded key", "text/plain")
  } else if (action === 'left_mouse_down') {
    await mouseDown();
    return getResult(id, "text", "left mouse down", "text/plain")
  } else if (action === 'left_mouse_up') {
    await mouseUp();
    return getResult(id, "text", "left mouse up", "text/plain")
  } 
}
async function processBashFunction(name, args, id) {
  var o = JSON.parse(args)
  let result = await Command.create('exec-sh', [
    '-c',
    o.command,
  ]).execute();
  return getResult(id, "text", result.stdout, "text/plain")
}

var currentFilePaths = {}
// 异步函数，获取文件路径并缓存得到的文件句柄，同一个文件的读写操作就不用每次都打开对话框
async function getFilePath(path, action) {
  if (currentFilePaths[path]) {
    return currentFilePaths[path];
  } else {
    if (action === 'save') {
      const filePath = await save({
        defaultPath: path,
      });
      if (filePath) {
        currentFilePaths[path] = filePath;
        return filePath;
      }
    } else if (action === 'open') {
      const filePath = await open({
        multiple: false,
        directory: false,
      });
      if (filePath) {
        currentFilePaths[path] = filePath;
        return filePath;
      }
    }
  }
  return null;
}

//文件操作类处理函数
async function processFileFunction(name, args, id) {
  var o = JSON.parse(args)
  var path = o.path;
  var dir = BaseDirectory.Desktop;
  if(path.indexOf('/') !== -1){
    path = path.split('/').slice(-1)[0];
    if(o.path.indexOf('Document') !== -1){
      dir = BaseDirectory.Document;
    } else if(o.path.indexOf('Download') !== -1){
      dir = BaseDirectory.Download;
    }
  }
  if (o.command === 'create') {
    await writeTextFile(path, o.file_text, {
      baseDir: dir
    });
    return getResult(id, "text", "file created.", "text/plain")
  } else if (o.command === 'view') {
    if (!(await exists(path, {baseDir:dir}))) {
      path = await getFilePath(path, 'open');
    }
    const text = await readTextFile(path, {baseDir:dir});
    if (o.view_range) {
      const lines = text.split('\n');
      const start = Math.max(0, o.view_range[0] - 1);
      const end = Math.min(lines.length, o.view_range[1] === -1 ? lines.length : o.view_range[1] - 1);
      const newText = lines.slice(start, end).join('\n');
      return getResult(id, "text", newText, "text/plain");
    } else {
      return getResult(id, "text", text, "text/plain");
    }
  } else if (o.command === 'str_replace') {
    if (!(await exists(path, {baseDir:dir}))) {
      path = await getFilePath(path, 'open');
    }
    const text = await readTextFile(path, {baseDir:dir});
    if (o.old_str) {
      const newText = text.replaceAll(o.old_str, o.new_str);
      await writeTextFile(path, newText, {baseDir:dir});
    } else {
      const newText = text + "\n" + o.new_str;
      await writeTextFile(path, newText, {baseDir:dir});
    }
    return getResult(id, "text", "file replaced.", "text/plain");
  } else if (o.command === 'insert') {
    if (!(await exists(path, {baseDir:dir}))) {
      path = await getFilePath(path, 'open');
    }
    const text = await readTextFile(path, {baseDir:dir});
    let lines = text.split('\n');
    if (o.insert_line < 1 || o.insert_line >= lines.length + 1) {
      console.log('插入行号无效')
      return getResult(id, "text", "insert_line out of range.", "text/plain");
    } else {
      // 在指定行后插入新文本
      lines.splice(o.insert_line, 0, o.new_str);
      // 将数组重新组合成字符串
      const newText = lines.join('\n');
      await writeTextFile(path, newText, {baseDir:dir});
      return getResult(id, "text", "text inserted.", "text/plain");
    }
  }
}

//测试方法，用于检测调用系统功能是否正常
async function testFunctions() {
  chatAnswers.value.push({ content: "{\"action\": \"screenshot\"}", type: 'action' })
  var t = await processFunction("computer", "{\"action\": \"screenshot\"}", "111");
  chatAnswers.value.push({ content: "{\"action\": \"mouse_move\", \"coordinate\": [200, 200]}", type: "action" })
  await processFunction("computer", "{\"action\": \"mouse_move\", \"coordinate\": [200, 200]}", "111");
  chatAnswers.value.push({ content: "{\"action\": \"left_click\"}", type: "action" })
  await processFunction("computer", "{\"action\": \"left_click\"}", "111");
  chatAnswers.value.push({ content: "{\"action\": \"double_click\"}", type: "action" })
  await processFunction("computer", "{\"action\": \"double_click\"}", "111");
  chatAnswers.value.push({ content: "{\"action\": \"left_click_drag\", \"coordinate\": [20, 200]}}", type: "action" })
  await processFunction("computer", "{\"action\": \"left_click_drag\", \"coordinate\": [20, 200]}", "111");
  chatAnswers.value.push({ content: "{\"action\": \"type\", \"text\": \"hello world\"}", type: "action" })
  await processFunction("computer", "{\"action\": \"type\", \"text\": \"hello world\"}", "111");
  chatAnswers.value.push({ content: "{\"action\": \"key\", \"text\": \"Return\"}", type: "action" })
  await processFunction("computer", "{\"action\": \"key\", \"text\": \"Return\"}", "111");
}
async function testNewFunctions() {
  chatAnswers.value.push({ content: "{\"action\": \"mouse_move\", \"coordinate\": [200, 200]}", type: "action" })
  await processFunction("computer", "{\"action\": \"mouse_move\", \"coordinate\": [200, 200]}", "111");
  chatAnswers.value.push({ content: "{\"action\": \"left_mouse_down\"}", type: "action" })
  await processFunction("computer", "{\"action\": \"left_mouse_down\"}", "111");
  chatAnswers.value.push({ content: "{\"action\": \"mouse_move\", \"coordinate\": [20, 20]}", type: "action" })
  await processFunction("computer", "{\"action\": \"mouse_move\", \"coordinate\": [20, 20]}", "111");
  chatAnswers.value.push({ content: "{\"action\": \"left_mouse_up\"}", type: "action" })
  await processFunction("computer", "{\"action\": \"left_mouse_up\"}", "111");
}
async function testCommands() {
  chatAnswers.value.push({ content: "{\"command\": \"create\"}", type: 'action' })
  await processFunction("str_replace_editor", "{\"command\":\"create\",\"path\":\"\\u4e0a\\u6d77\\u5929\\u6c14\\u9884\\u62a5.txt\",\"file_text\":\"\\u4e0a\\u6d77\\u5929\\u6c14\\u9884\\u62a5 (2024\\u5e7410\\u670830\\u65e5)\\n\\n\\u73b0\\u5728\\u5929\\u6c14\\u60c5\\u51b5\\uff1a\\n\\u5929\\u6c14\\uff1a\\u591a\\u4e91\\n\\u6e29\\u5ea6\\uff1a18\\u6444\\u6c0f\\u5ea6\\n\\n\\u4eca\\u5929\\u5929\\u6c14\\u9884\\u62a5\\uff1a\\n\\u767d\\u5929\\uff1a\\u9634\\n\\u591c\\u95f4\\uff1a\\u9634\\n\\u6e29\\u5ea6\\uff1a15-22\\u6444\\u6c0f\\u5ea6\\n\\u98ce\\u5411\\uff1a\\u4e1c\\u5317\\u98ce4\\u7ea7\\n\\u7a7f\\u8863\\u5efa\\u8bae\\uff1a\\u5efa\\u8bae\\u7740\\u8584\\u5916\\u5957\\u3001\\u5f00\\u886b\\u725b\\u4ed4\\u886b\\u88e4\\u7b49\\u670d\\u88c5\\u3002\\u5e74\\u8001\\u4f53\\u5f31\\u8005\\u5e94\\u9002\\u5f53\\u6dfb\\u52a0\\u8863\\u7269\\uff0c\\u5b9c\\u7740\\u5939\\u514b\\u886b\\u3001\\u8584\\u6bdb\\u8863\\u7b49\\u3002\\n\\n\\u660e\\u5929\\u5929\\u6c14\\u9884\\u62a5\\uff1a\\n\\u767d\\u5929\\uff1a\\u4e2d\\u96e8\\n\\u591c\\u95f4\\uff1a\\u5927\\u96e8\\n\\u6e29\\u5ea6\\uff1a18-21\\u6444\\u6c0f\\u5ea6\\n\\u98ce\\u5411\\uff1a\\u4e1c\\u5317\\u98ce4\\u7ea7\\n\\u6ce8\\u610f\\u4e8b\\u9879\\uff1a\\u6709\\u8f83\\u5f3a\\u964d\\u6c34\\uff0c\\u4e0d\\u9002\\u5b9c\\u667e\\u6652\\u3002\\u8bf7\\u643a\\u5e26\\u96e8\\u5177\\u51fa\\u884c\\u3002\\n\\n\\u540e\\u5929\\u5929\\u6c14\\u9884\\u62a5\\uff1a\\n\\u767d\\u5929\\uff1a\\u66b4\\u96e8\\n\\u591c\\u95f4\\uff1a\\u5c0f\\u96e8\\n\\u6e29\\u5ea6\\uff1a17-20\\u6444\\u6c0f\\u5ea6\\n\\u98ce\\u5411\\uff1a\\u5317\\u98ce4\\u7ea7\\n\\u6ce8\\u610f\\u4e8b\\u9879\\uff1a\\u6709\\u66b4\\u96e8\\u5929\\u6c14\\uff0c\\u8bf7\\u6ce8\\u610f\\u9632\\u8303\\uff0c\\u51fa\\u884c\\u643a\\u5e26\\u96e8\\u5177\\u3002\\n\\n\\u6e29\\u99a8\\u63d0\\u793a\\uff1a\\n1. \\u8fd1\\u671f\\u5929\\u6c14\\u591a\\u96e8\\uff0c\\u8bf7\\u968f\\u8eab\\u643a\\u5e26\\u96e8\\u5177\\n2. \\u6e29\\u5ea6\\u9002\\u4e2d\\uff0c\\u65e0\\u9700\\u5f00\\u542f\\u7a7a\\u8c03\\n3. \\u7a7a\\u6c14\\u8d28\\u91cf\\u826f\\u597d\\uff0c\\u9002\\u5408\\u6237\\u5916\\u6d3b\\u52a8\\n4. \\u8bf7\\u6ce8\\u610f\\u9632\\u96e8\\uff0c\\u505a\\u597d\\u9632\\u62a4\\u63aa\\u65bd\"}", "111")
  chatAnswers.value.push({ content: "{\"command\": \"view\"}", type: 'action' })
  await processFunction("str_replace_editor", "{\"command\":\"view\",\"path\":\"/User/jason/Desktop/\\u4e0a\\u6d77\\u5929\\u6c14\\u9884\\u62a5.txt\"}", "111")
  chatAnswers.value.push({ content: "{\"command\": \"str_replace\"}", type: 'action' })
  await processFunction("str_replace_editor", "{\"command\":\"str_replace\",\"path\":\"/User/jason/Desktop/\\u4e0a\\u6d77\\u5929\\u6c14\\u9884\\u62a5.txt\",\"old_str\":\"\",\"new_str\":\"\\u5408\\u80a5\\u5929\\u6c14\\u9884\\u62a5 (2024\\u5e7410\\u670830\\u65e5\\u66f4\\u65b0)\\n\\n\\u73b0\\u5728\\u5929\\u6c14\\u72b6\\u51b5\\uff1a\\n- \\u5929\\u6c14\\uff1a\\u6674\\n- \\u6e29\\u5ea6\\uff1a17\\u6444\\u6c0f\\u5ea6\\n\\n\\u4eca\\u5929\\u9884\\u62a5\\uff1a\\n- \\u767d\\u5929\\uff1a\\u6674\\n- \\u591c\\u95f4\\uff1a\\u591a\\u4e91\\n- \\u6e29\\u5ea6\\uff1a10-22\\u6444\\u6c0f\\u5ea6\\n- \\u98ce\\u5411\\uff1a\\u4e1c\\u98ce4\\u7ea7\\n- \\u7a7f\\u8863\\u5efa\\u8bae\\uff1a\\u5efa\\u8bae\\u7740\\u8584\\u5916\\u5957\\u3001\\u5f00\\u886b\\u725b\\u4ed4\\u886b\\u88e4\\u7b49\\u670d\\u88c5\\n- \\u9632\\u6652\\u6307\\u6570\\uff1a\\u7d2b\\u5916\\u7ebf\\u5f3a\\u5ea6\\u8f83\\u5f31\\uff0c\\u5efa\\u8bae\\u6d82\\u64e6SPF12-15\\u3001PA+\\u9632\\u6652\\u62a4\\u80a4\\u54c1\\n\\n\\u660e\\u5929\\u9884\\u62a5\\uff1a\\n- \\u767d\\u5929\\uff1a\\u591a\\u4e91\\n- \\u591c\\u95f4\\uff1a\\u591a\\u4e91\\n- \\u6e29\\u5ea6\\uff1a11-22\\u6444\\u6c0f\\u5ea6\\n- \\u98ce\\u5411\\uff1a\\u4e1c\\u5317\\u98ce2\\u7ea7\\n\\n\\u540e\\u5929\\u9884\\u62a5\\uff1a\\n- \\u767d\\u5929\\uff1a\\u591a\\u4e91\\n- \\u591c\\u95f4\\uff1a\\u6674\\n- \\u6e29\\u5ea6\\uff1a9-19\\u6444\\u6c0f\\u5ea6\\n- \\u98ce\\u5411\\uff1a\\u5317\\u98ce1\\u7ea7\"}", "111")
}

async function handleMenu(value) {
  if (value === 'test') {
    await testFunctions();
    await testNewFunctions();
    await testCommands();
  } else if (value === 'tasks') {
    await openTasksWindow();
  } else if (value === 'sample-web') {
    prompt.value = '使用我的Chrome浏览器，打开百度网站，搜索DeepSeek，打开第二个搜索结果。'
  } else if (value === 'sample-file') {
    prompt.value = '查询一下上海和合肥的天气，将详细结果用文本文件保存到我的文档目录里。再将结果中的信息按照城市、日期、最高温、最低温、天气情况的格式保存成一个csv文件到我的文档目录里。'
  }else if (value === 'export-txt'){
    if(chatAnswers.value.length === 0){
      Message.error('当前没有对话记录');
    }else{
      var path = "AI聊天记录.txt"
      var content = ''
      for (var i = 0; i < chatAnswers.value.length; i++) {
        content += chatAnswers.value[i].type + ':\n' + chatAnswers.value[i].content + '\n\n';
      }
      const filePath = await save({
        defaultPath: path,
      });
      if (filePath) {
        await writeTextFile(filePath, content);
        Message.success('文件保存成功');
      }
    }
  }
}

const hasToken = ref(false);

var unlisten;
var currentTaskIndex = -1;
var currentAutoNext = false;
onUnmounted(() => {
  unlisten();
});
//启动时加载显示器信息，和创建动态任务的监听接口
onMounted(async () => {
  hasToken.value = global.getToken().length > 0;
  setTimeout(() => {
    chatListMinHeight.value = window.innerHeight - divCounter.value?.$el.clientHeight - 32;
  }, 20);
  getMonitors().then((ms) => {
    monitors.value = ms;
    const primaryMonitor = ms.find((m) => m.is_primary);
    if (primaryMonitor) {
      selectedMonitorId.value = primaryMonitor.id;
      checkMonitorSize(primaryMonitor);
    }
  });
  unlisten = await listen<string>('to-main', (event) => {
    var pay = event.payload;
    if (pay.type === 'run-task') {
      currentTaskIndex = pay.message.index;
      currentAutoNext = pay.message.autoNext;
      var task = pay.message.task;
      clearChatContexts();
      prompt.value = task;
      autoWorkMode.value = true;
      startChat('tasks');
    }
  });
})
function checkMonitorSize(monitor) {
  if (monitor && monitor.width > 1440 && monitor.height > 900) {
    monitorSizeTips.value = '当前显示器分辨率：' + monitor.width + 'x' + monitor.height + '，分辨率过高会严重影响使用效果，请调低分辨率后使用，建议分辨率设置为不超过1440x900。\n或者切换到 **裁剪** 模式使用。\n**注意：每次调整分辨率后务必退出并重新启动应用**。' ;
    showMonitorSizeTips.value = true;
  }else{
    showMonitorSizeTips.value = false;
  }
}

async function selectedMonitorChanged(value) {
  const selectedMonitor = monitors.value.find((m) => m.id === value);
  checkMonitorSize(selectedMonitor);
}

async function useCutModeChange(value) {
  if (!value) {
    const selectedMonitor = monitors.value.find((m) => m.id === selectedMonitorId.value);
    checkMonitorSize(selectedMonitor);
  }else{
    showMonitorSizeTips.value = false;
  }
}

async function autoWorkModeChange(value) {
  clearChatContexts();
  if (value) {
    const selectedMonitor = monitors.value.find((m) => m.id === selectedMonitorId.value);
    checkMonitorSize(selectedMonitor);
  }
}

async function openTasksWindow() {
  const webview = new WebviewWindow('tasks', {
    url: '/#/tasks',
    title: '批量任务',
    width: 460,
    height: 800
  })
  webview.once('tauri://created', function () {
    // webview window successfully created
  });
  webview.once('tauri://error', function (e) {
    Message.error('创建窗口失败：' + e.payload);
  });
}

async function movePositionTo1280() {
  const selectedMonitor = monitors.value.find((m) => m.id === selectedMonitorId.value);
  await getCurrentWindow().setPosition(new LogicalPosition((selectedMonitor?.x??0)+1280, (selectedMonitor?.y??0)+800));
}

</script>

<style scoped>
.container {
  margin: 0;
  padding: 8px;
  display: flex;
  flex-direction: column;
}

.action,
.error {
  margin-right: 40px;
  background-color: #fafafa;
  border-radius: 10px;
  padding: 8px;
  color: #3f4a56;
}

.action {
  background-color: #fafafa;
  font-size: 12px;
}

.input {
  border: solid 1px #f0f0f0;
  margin-top: 10px;
  padding: 8px;
}

.buttons {
  display: flex;
  flex-direction: row;
  justify-content: space-between;
}
:deep() .md-editor-preview-wrapper{
  padding: 0;
}
:deep() .md-editor-preview {
  font-size: 14px;
  padding: 6px;
  border-left: none;
}

:deep() .md-editor-preview p {
  line-height: 180% !important;
}

.user :deep() .md-editor-preview {
  margin-left: 40px;
  border-left: solid 2px #f0f0f0;
}

.assistant :deep() .md-editor-preview {
  margin-right: 40px;
}

.login {
  justify-content: center;
}
</style>
