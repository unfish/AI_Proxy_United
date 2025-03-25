import global from "./util";

/*
ps:{prompt:'xxx', withContext:bool, displayWidth:xxx, displayHeight:xxx}
processCallback:每收到一个片段就调用该回调方法，参数是当前这条字符串
finishCallback:全部接收完毕以后调用该回调方法，参数是完整的结果字符串
errorCallback:如果出现错误就调用该回调方法，参数是错误提示字符串
functionCallback:如果传入的参数使用了withFunctions功能，并且匹配到了对应的方法，调用该回调方法，参数是需要调用的方法的name,arguments,id, arguments是个文本，如果是对象需要自己再parse一次
 */
export function askGpt(ps, processCallback, finishCallback, errorCallback, functionCallback) {
    ps.prompt = ps.prompt || ''
    ps.withPromptKey = ps.withPromptKey || ''
    ps.chatModel = ps.chatModel ||  global.agentModel
    var body = {"question":ps.prompt, 
        "chatModel": ps.chatModel,
        "questionType" : 0, 
        "chatFrom" : 0,
        "withContext": ps.withContext,
        "displayWidth": ps.displayWidth || 0,
        "displayHeight": ps.displayHeight || 0,
    };
    doRequest('api/ai/chat', body, processCallback, finishCallback, errorCallback, functionCallback);
}

/*
ps:[{tool_id:'xxx', result_type:'text|image', content:'xxx'}]
processCallback:每收到一个片段就调用该回调方法，参数是当前这条字符串
finishCallback:全部接收完毕以后调用该回调方法，参数是完整的结果字符串
errorCallback:如果出现错误就调用该回调方法，参数是错误提示字符串
functionCallback:如果传入的参数使用了withFunctions功能，并且匹配到了对应的方法，调用该回调方法，参数是需要调用的方法的name,arguments,id, arguments是个文本，如果是对象需要自己再parse一次
 */
export function setToolResult(ps, processCallback, finishCallback, errorCallback, functionCallback) {
    var body = {
        "chatModel": global.agentModel,
        "chatFrom" : 0,
        "toolResults": ps
    };
    doRequest('api/ai/SetToolResults', body, processCallback, finishCallback, errorCallback, functionCallback);
}


function doRequest(url, body, processCallback, finishCallback, errorCallback, functionCallback) {
    fetch(global.hostUrl+url, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'x-access-token':global.getToken()
        },
        body: JSON.stringify(body)
    }).then((resp) => {
        // 处理响应流数据
        return new Promise((resolve, reject) => {
            const reader = resp.body.getReader() // 创建了一个可读流的阅读器对象
            var result = ''
            var logId = ''
            var sessionId = ''
            const processResult = ({done, value}) => {
                if (done) {
                    finishCallback(result, logId, sessionId)
                    return
                }
                // 处理读取到的数据块
                var str = new TextDecoder().decode(value)
                //console.log(str);
                var start = str.indexOf("data: ");
                while(start>=0){
                    var lastIndex = str.indexOf('\n', start);
                    if(lastIndex-start-6===0){
                        processCallback('\n', 'Answer')
                        result+='\n'
                    }else{
                        var subStr = str.substr(start+6, lastIndex-start-6)
                        if(subStr==='[DONE]'){
                            break
                        }else if(subStr.startsWith("{")){
                            var o = JSON.parse(subStr);
                            if(o.resultType==='FuncFrontend' && functionCallback){
                                var fc = JSON.parse(o.result)
                                functionCallback(fc.name, fc.arguments, fc.id)
                            }else if(o.resultType==='LogSaved'){
                                var fc = JSON.parse(o.result)
                                logId = fc.Id;
                                sessionId = fc.SessionId;
                            }else{
                                processCallback(o.result, o.resultType)
                                result+=o.result
                            }
                        }else{
                            var con1 = subStr
                            if(con1){
                                processCallback(con1, 'Answer')
                                result+=con1
                            }
                        }
                    }
                    str = str.substr(lastIndex+1)
                    start = str.indexOf("data: ");
                }
                reader.read().then(processResult) // 直至流读取完毕
            }
            reader.read().then(processResult) // read() 从流中读取了第一个数据块
        })
    })
        .catch((error) => {
            console.log('gpt', error)
            errorCallback(error)
        })
}