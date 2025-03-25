<template>
    <div class="info-page">
        <a-space fill class="buttons">
            <a-button type="outline" @click="loadTasks('batch')">加载批量任务</a-button>
            <a-button type="outline" @click="loadTasks('test')">加载测试用例</a-button>
            <a-button type="dashed" shape="circle" @click="showHelp()">
                <template #icon>
                    <icon-question />
                </template>
            </a-button>
            <a-button type="primary" @click="runTask(0, true)" :loading="taskRunning" :disabled="!Tasks.length">
                <template #icon>
                    <icon-forward />
                </template>
                <template #default>运行</template>
            </a-button>
        </a-space>
        <a-table :columns="columns" :data="Tasks" :pagination="false">
            <template #task="{ record, rowIndex }">
                <div v-if="record.title" class="title">
                    {{ record.title }}
                </div>
                <div>
                    {{ record.task.length > 50 ? record.task.substring(0, 50) + '...' : record.task }}
                </div>
            </template>
            <template #optional="{ record, rowIndex }">
                <div style="text-align: center;">
                    <a-button @click="runTask(rowIndex, false)" v-if="record.status === 'waiting'"
                        :loading="taskRunning">
                        <template #icon>
                            <icon-play-arrow />
                        </template>
                    </a-button>
                    <icon-loading v-else-if="record.status === 'running'" />
                    <icon-check v-else-if="record.status === 'success'" />
                    <template v-else>
                        <a-popover>
                            <icon-close />
                            <template #content>
                                <p>{{ record.result }}</p>
                            </template>
                        </a-popover>
                    </template>
                </div>
            </template>
        </a-table>
    </div>
    <a-drawer :width="380" :hide-cancel="true" :closable="true" :mask-closable="true" :visible="showHelpDrawer"
        @ok="hideHelp" unmountOnClose>
        <template #header>
            <span>使用说明</span>
        </template>
        <div>
            <p>1. 加载任务文件，支持批量任务和测试用例两种格式。</p>
            <p>2. 批量任务格式：每个任务以两个空行进行分割，每个任务的第一行以#开头作为标题，标题在任务运行时会排除，正文部分就是要执行的动作。</p>
            <p>3. 测试用例格式：每个测试用例以两个空行进行分割，第一行以#开头作为标题。测试用例需要使用&lt;action&gt;和&lt;expect&gt;对来指定需要执行的动作，和执行后的判断结果。</p>
            <p>4. 动作和期望结果的描述都可以使用数字序号来更清晰的指定动作顺序和多条结果。</p>
            <p>5. 点击“Go!”按钮开始按顺序执行全部任务，点击任务行中的按钮开始执行单个任务。</p>
            <a-space fill class="buttons">
                <a-button type="outline" @click="saveTasksTemplate('batch')">保存批量任务模板</a-button>
                <a-button type="outline" @click="saveTasksTemplate('test')">保存测试任务模板</a-button>
            </a-space>
        </div>
    </a-drawer>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from "vue";
import { emit, listen } from '@tauri-apps/api/event';
import { save, open } from '@tauri-apps/plugin-dialog';
import { writeTextFile, readTextFile } from '@tauri-apps/plugin-fs';

type Task = {
    title?: string;
    task: string;
    status: string;
    result?: string;
};

const Tasks = ref<Task[]>([]);

const columns = [{
    title: '任务',
    slotName: 'task',
}, {
    title: '状态',
    slotName: 'optional',
    width: 80
}];

var currentTaskType = 'batch';
async function loadTasks(type) {
    const filePath = await open({
        multiple: false,
        directory: false,
    });
    if (filePath) {
        currentTaskType = type;
        const text = await readTextFile(filePath);
        const paras = text.split('\n\n\n');
        var tasks: Task[] = [];
        for (var para of paras) {
            if (para.trim().length === 0) {
                continue;
            }
            const lines = para.trim().split('\n');
            if (lines.length > 0 && lines[0].startsWith('#')) {
                tasks.push({
                    title: lines[0],
                    task: lines.slice(1).join('\n'),
                    status: 'waiting'
                });
            } else {
                tasks.push({
                    task: para,
                    status: 'waiting'
                });
            }
        }
        Tasks.value = tasks;
    }
}

async function saveTasksTemplate(type) {
    var template = '';
    var filename = '';
    if (type === 'batch') {
        filename = '批量任务模板.txt';
        template = `#任务1
1. 动作1
2. 动作2


#任务2
1. 动作1
2. 动作2`;
    } else if (type === 'test') {
        filename = '测试任务模板.txt';
        template = `#任务1
<action>
1. 动作1
2. 动作2
</action>
<expect>
1. 期望结果1
2. 期望结果2
</expect>


#任务2
<action>
1. 动作1
2. 动作2
</action>
<expect>
1. 期望结果1
2. 期望结果2
</expect>`;
    }
    const filePath = await save({
        defaultPath: filename,
    });
    if (filePath) {
        await writeTextFile(filePath, template);
    }
}

const showHelpDrawer = ref(false);
async function showHelp() {
    showHelpDrawer.value = true
}
async function hideHelp() {
    showHelpDrawer.value = false
}

var taskRunning = false;
async function runTask(index: number, autoNext: boolean = false) {
    if (index >= Tasks.value.length) {
        return;
    }
    taskRunning = true
    Tasks.value[index].status = 'running';
    var content = Tasks.value[index].task;
    if (currentTaskType === 'test') {
        content = '请按照以下内容中的<action>来执行动作，并按照<expect>中的内容来检查结果。当动作全部完成后检查所有的期望结果中的内容是否都符合期望的描述，如果全部符合，请在最后输出<status>success</status>，如果不符合，不用输出status。\n\n' + content;
    } else {
        content = '请按照以下内容中的描述来执行动作。如果所有动作都成功执行完成，请在最后输出<status>success</status>，如果因为各种原因中断无法成功执行所有动作，不用输出status。\n\n' + content;
    }
    await emit('to-main', { type: 'run-task', message: { task: content, index, autoNext } });
}

var unlisten;
onMounted(async () => {
    unlisten = await listen<string>('to-tasks', (event) => {
        var pay = event.payload;
        if (pay.type === 'task-result') {
            var index = pay.message.index;
            var result = pay.message.result;
            var status = result.indexOf('<status>success</status>') >= 0 ? 'success' : 'fail';
            Tasks.value[index].status = status;
            Tasks.value[index].result = result;
            taskRunning = false;
            if (pay.message.autoNext) {
                var nextIndex = index + 1;
                if (nextIndex < Tasks.value.length) {
                    runTask(nextIndex, true);
                }
            }
        }
    });
});
onUnmounted(() => {
    unlisten();
});

</script>

<style scoped>
.title {
    font-weight: bold;
}

.buttons {
    padding: 8px;
    display: flex;
    flex-direction: row;
    justify-content: space-between;
}
</style>