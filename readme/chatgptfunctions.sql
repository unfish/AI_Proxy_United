INSERT INTO `chatgptfunctions` (`Id`, `GroupName`, `Name`, `Description`, `Parameters`, `FunctionType`, `CallMethod`, `CallUrl`, `Disabled`, `CreatedOn`, `FunctionPrompt`, `TriggerWords`, `UseResultDirect`) VALUES ('1','Internal','GetWeather','获取指定城市或位置的天气情况','{
    "type":"object",
    "required":["city"],
    "properties":{
        "city":{
            "type":"string",
            "description":"城市名或区县名，比如上海或上海闵行，返回城市的中文名"
        }
    }
}','2','0','','0','2023-07-16 08:43:16','天气的温度描述可以简洁一点，比如每天的最低气温和最高气温，可以直接使用[最低气温的数字]℃～[最高气温的数字]℃来表示，其它建议保留。','天气','0');
INSERT INTO `chatgptfunctions` (`Id`, `GroupName`, `Name`, `Description`, `Parameters`, `FunctionType`, `CallMethod`, `CallUrl`, `Disabled`, `CreatedOn`, `FunctionPrompt`, `TriggerWords`, `UseResultDirect`) VALUES ('7','Internal','DrawImage','调用外部功能来画画，根据用户指定的描述语画一幅画。注意，该功能**不能**用来画流程图。','{
    "type":"object",
    "required":["prompt"],
    "properties":{
        "prompt":{
            "type":"string",
            "description":"用户提供的需要画画的描述语，通常需要是一个完整的场景描述，你可以补充其细节描述。描述语可以使用中文，也可以用英文。"
        }
    }
}','2','0',null,'0','2023-09-21 21:09:01',null,'画画,画一张,画一个,画一幅','1');
INSERT INTO `chatgptfunctions` (`Id`, `GroupName`, `Name`, `Description`, `Parameters`, `FunctionType`, `CallMethod`, `CallUrl`, `Disabled`, `CreatedOn`, `FunctionPrompt`, `TriggerWords`, `UseResultDirect`) VALUES ('9','Internal','CreateTask','给用户创建待办事项、待办任务、或提醒','{
    "type":"object",
    "required":["summary","endDate"],
    "properties":{
        "summary":{
            "type":"string",
            "description":"需要处理的任务、提醒、待办事项的主题"
        },
        "endDate":{
            "type":"string",
            "description":"待办事项的截止时间的日期部分，日期格式为YYYY-MM-DD，如果用户只说了月和日，则默认为今年的几月几号。"
        },
        "endTime":{
            "type":"string",
            "description":"待办事项的截止时间的具体时间，小时和分钟部分，格式为HH:mm，如果用户只说了几点，则分钟默认为00。如果用户没有提供具体时间，默认为09:00"
        }
    }
}','0','0',null,'0','2023-11-11 21:34:51',null,'提醒,待办','0');
INSERT INTO `chatgptfunctions` (`Id`, `GroupName`, `Name`, `Description`, `Parameters`, `FunctionType`, `CallMethod`, `CallUrl`, `Disabled`, `CreatedOn`, `FunctionPrompt`, `TriggerWords`, `UseResultDirect`) VALUES ('11','Internal','CreateEvent','给用户在日历中创建一条日程或会议提醒','{
    "type":"object",
    "required":["summary","startDate"],
    "properties":{
        "summary":{
            "type":"string",
            "description":"需要创建的日程或会议的主题"
        },
        "startDate":{
            "type":"string",
            "description":"日程或会议的开始时间的时间的日期部分，日期格式为YYYY-MM-DD，如果用户只说了月和日，则默认为今年的几月几号。"
        },
        "startTime":{
            "type":"string",
            "description":"日程或会议的开始时间的具体时间，小时和分钟部分，格式为HH:mm，如果用户只说了几点，则分钟默认为00。如果用户没有提供具体时间，默认为09:00"
        }
    }
}','0','0',null,'0','2023-11-13 10:21:40',null,'日程,会议','0');
INSERT INTO `chatgptfunctions` (`Id`, `GroupName`, `Name`, `Description`, `Parameters`, `FunctionType`, `CallMethod`, `CallUrl`, `Disabled`, `CreatedOn`, `FunctionPrompt`, `TriggerWords`, `UseResultDirect`) VALUES ('19','Internal','DrawChart','使用指定的数据生成一张可交互的动态图表','{
    "type": "object",
    "required": [
        "data"
    ],
    "properties": {
        "data": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "x": {
                        "type": "string",
                        "description": "图表的X轴变量名，通常为数据的维度名称，比如日期，产品品类，姓名等"
                    },
                    "y": {
                        "type": "number",
                        "description": "图表的Y轴变量名，通常为数值，比如温度，金额，价格，分数等等"
                    },
                    "series": {
                        "type": "string",
                        "description": "同一个X轴变量下，Y轴变量的属性，不同属性表现为两组柱图或两条折线等，比如温度是最高温度还是最低温度，金额是销售额还是毛利额，分数是语文还是数学等等"
                    }
                }
            }
        },
        "title":{
            "type":"string",
            "description":"图表的名称，比如温度变化图，销售额统计图，学生成绩分布图"
        },
        "charttype":{
            "type":"string",
            "enum":["折线图","柱状图","饼图","瀑布图"],
            "description":"图表的类型，比如折线图，柱状图，饼图,瀑布图等"
        }
    }
}
','0','0',null,'0','2024-03-27 18:09:59',null,'图表,折线图,柱状图','0');
INSERT INTO `chatgptfunctions` (`Id`, `GroupName`, `Name`, `Description`, `Parameters`, `FunctionType`, `CallMethod`, `CallUrl`, `Disabled`, `CreatedOn`, `FunctionPrompt`, `TriggerWords`, `UseResultDirect`) VALUES ('20','Internal','ChangeModel','切换模型为指定ID，切换本地使用的默认模型的编号','{
    "type":"object",
    "properties":{
        "model":{
            "type":"number",
            "description":"要切换的指定模型的编号，是一个0到300之间的数字，比如切换到1号模型。没有则默认为-1"
        },
        "name":{
            "type":"string",
            "enum":["GPT 3.5","GPT 4","MiniMax","阿里通义","百度文心","知识库"],
            "description":"要切换的指定模型的名称，比如切换到知识库，或者切换到GPT4。与编号必须存在其中一个"
        }
    }
}','0','0',null,'0','2024-05-08 15:40:45',null,'切换到','0');
INSERT INTO `chatgptfunctions` (`Id`, `GroupName`, `Name`, `Description`, `Parameters`, `FunctionType`, `CallMethod`, `CallUrl`, `Disabled`, `CreatedOn`, `FunctionPrompt`, `TriggerWords`, `UseResultDirect`) VALUES ('22','Internal','GoogleSearch','使用Google搜索，获取最新及最可靠的互联网信息，来补充大模型的知识进行未知问题及实时问题的回复。','{
    "type":"object",
    "required":["q"],
    "properties":{
        "q":{
            "type":"string",
            "description":"要搜索的关键词，需要以适合google搜索引擎的方式提供，不要同时搜索多个不同方向的关键词，需要拆分成多次搜索结果更准确，核心关键词使用双引号括起来提高搜索结果相关性。"
        }
    }
}','2','0',null,'0','2024-05-14 08:19:45',null,'Google搜索','0');
INSERT INTO `chatgptfunctions` (`Id`, `GroupName`, `Name`, `Description`, `Parameters`, `FunctionType`, `CallMethod`, `CallUrl`, `Disabled`, `CreatedOn`, `FunctionPrompt`, `TriggerWords`, `UseResultDirect`) VALUES ('27','Internal','MathCalculator','数学计算器/简易代码执行器','{
    "type":"object",
    "required":["formula"],
    "properties":{
        "formula":{
            "type":"string",
            "description":"需要计算的公式，如 \'5 + 2 * 8 / (80 + 9.9)\'，\\n也可以使用C#语法的数学计算逻辑，如 \'Math.Pow(4,4) * 8\', 仅支持C#原生Math类中的方法，不需要写return，直接输入计算逻辑代码即可。\\n也可以使用简单表达式，比如\'string.Format(\\"My name is {0}. Today is {1}\\", \\"R2-D2\\", DateTime.Now.ToShortDateString())\'。\\n一次调用只能使用一个表达式，只会返回单一变量结果。"
        }
    }
}','2','0','fake','0','2024-09-21 07:54:45',null,'计算,算一下,外部函数','0');
INSERT INTO `chatgptfunctions` (`Id`, `GroupName`, `Name`, `Description`, `Parameters`, `FunctionType`, `CallMethod`, `CallUrl`, `Disabled`, `CreatedOn`, `FunctionPrompt`, `TriggerWords`, `UseResultDirect`) VALUES ('28','Internal','ZhipuSearch','使用智谱搜索，获取中文互联网上的最新信息，来补充大模型的知识进行未知问题及实时问题的回复。','{
    "type":"object",
    "required":["q"],
    "properties":{
        "q":{
            "type":"string",
            "description":"要搜索的关键信息，需要以适合google搜索引擎的方式提供"
        }
    }
}','2','0',null,'0','2025-02-20 15:51:59',null,'智谱搜索','0');
INSERT INTO `chatgptfunctions` (`Id`, `GroupName`, `Name`, `Description`, `Parameters`, `FunctionType`, `CallMethod`, `CallUrl`, `Disabled`, `CreatedOn`, `FunctionPrompt`, `TriggerWords`, `UseResultDirect`) VALUES ('29','Internal','SearchAndSummarize','搜索摘要，使用Google搜索，获取最新及最可靠的互联网信息，来补充大模型的知识进行未知问题及实时问题的回复，对搜索结果进行摘要后只返回关键信息，避免搜索结果原文太长。','{
    "type":"object",
    "required":["q", "target"],
    "properties":{
        "q":{
            "type":"string",
            "description":"要搜索的关键词，需要以适合google搜索引擎的方式提供，不要同时搜索多个不同方向的关键词，需要拆分成多次搜索结果更准确，核心关键词使用双引号括起来提高搜索结果相关性。"
        },
        "target":{
            "type":"string",
            "description":"本次搜索需要回答的问题，需要查找与汇总的关键数据，以及其它必须保留用来最后总结的关键结论，例如:不要只说返回企业经营数据，而是明确要求返回销售额、利润额、市场占有率、增长率等等明确指标。需求描述尽量详细完整，如果有必要可以指定数据返回的格式，否则可能会在摘要过程中遗漏关键信息，例如:使用markdown表格形式返回各国历年GDP数据。"
        }
    }
}','2','0',null,'0','2025-03-07 14:54:32',null,'搜索摘要','0');
INSERT INTO `chatgptfunctions` (`Id`, `GroupName`, `Name`, `Description`, `Parameters`, `FunctionType`, `CallMethod`, `CallUrl`, `Disabled`, `CreatedOn`, `FunctionPrompt`, `TriggerWords`, `UseResultDirect`) VALUES ('30','Internal','OneAgent','万能助理，可以设定不同的身份，每种身份拥有专有的技能，可以调用多个不同的身份联合来完成一项复杂的任务。你可以一次性生成多个助理安排好所有的工作，也可以只在需要的时候生成所需要助理，等它完成工作以后再来生成下一步的助理。','{
    "type":"object",
    "required":["role","skill","task","need_contexts"],
    "properties":{
        "role":{
            "type":"string",
            "description":"该助理要在任务中承担的角色，比如主持人、产品经理、程序员、测试人员等"
        },
        "task":{
            "type":"string",
            "description":"该助理本次需要承担的任务的详细描述，包括任务的目标、任务的背景、任务的要求等，助理将不依赖其它任务提示来完成该任务。"
        },
        "skill":{
            "type":"string",
            "enum":["信息搜集","操作助手","文件助手","方案设计","代码编写","文档编写","审阅者","计算者"],
            "description":"该角色所需要的技能。其中每种技能所包含的能力：\\n信息搜集：可以调用Google查找相关的信息并回答指定的问题，能够自动拆解复杂的搜索问题进行多步骤搜索后合并答案。但是它只负责收集信息，不负责写报告和方案。\\n操作助手：可以调用浏览器。对于Google搜索无法解决的问题，比如需要打开某个特定的网站查找信息，该角色可以自动调用浏览器打开指定的网址并在网站内自动导航找到所需的信息。请给出具体的网址，以及尽量明确的操作路径与所需达成的结果。该浏览器无法打开Google搜索引擎页面。如果是宽泛的信息搜集任务，应优先使用信息搜集角色。\\n文件助手：可以操作读写本地文件。对于文件读写任务，请给出文件名和要进行的修改，比如将本阶段任务完成结果更新进todo.md文件中对应的位置。\\n方案设计：可以根据指定的需求及背景信息设计出合理的解决方案，能够主动思考并自动向用户追问来补充客户未提到的潜在需求。\\n代码编写：可以根据指定的项目方案编写出合理的高质量的代码。\\n文档编写：可以根据指定的需求以及所有可用的信息，编写出结构合理界面美观的文档。但是它没有搜集信息的能力。如果需要控制界面美观度，请指定使用HTML格式来输出文档，并说明想要的美观度与风格。\\n审阅者：可以根据指定的需求对文档、代码等进行审阅，提出合理的修改意见。\\n计算者：可以计算指定的数学表达式并给出精确的结果。"
        },
        "need_contexts":{
            "type":"array",
            "items":{
                "type":"string"
            },
            "description":"该角色是否需要前面的某个角色的输出作为输入，比如产品经理需要主持人的需求文档，程序员需要产品经理的方案设计文档等，列出需要引用上下文的角色名称，在描述任务需求时就不需要重复这一部分的内容。"
        }
    }
}','2','0',null,'0','2025-03-13 13:08:06',null,'万能助理','0');
INSERT INTO `chatgptfunctions` (`Id`, `GroupName`, `Name`, `Description`, `Parameters`, `FunctionType`, `CallMethod`, `CallUrl`, `Disabled`, `CreatedOn`, `FunctionPrompt`, `TriggerWords`, `UseResultDirect`) VALUES ('31','Internal','SaveResultToFile','将助理任务的报告结果追加写入到指定的文件并自动将最新的文件发送给用户，如果文件不存在会自动创建。不需要重复要写入的内容，只需要指定助理的角色名称，会自动获取到上下文中该助理输出的结果并加入文件。如果追加内容，不需要更新或编辑原有内容，优先使用本方法而不是文件助手。','{
    "type":"object",
    "required":["role","title","filename"],
    "properties":{
        "role":{
            "type":"string",
            "description":"本次要保存到文件的内容对应的角色名称，比如主持人、产品经理、程序员、测试人员等"
        },
        "title":{
            "type":"string",
            "description":"本次内容对应的报告的主题，或分步骤的段落标题。"
        },
        "filename":{
            "type":"string",
            "description":"需要写入内容的文件名，使用相对路径。"
        }
    }
}','2','0',null,'0','2025-03-24 11:05:01',null,'万能助理','0');

