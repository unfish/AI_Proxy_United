function addScript(link, callback){
    var head = document.head || document.getElementsByTagName('head')[0];
    var script = document.createElement('script');
    script.addEventListener("load", callback, false);
    script.setAttribute("src", link);
    head.appendChild(script);
}
function appendMermaidScript() {
    addScript("/api/ai/static/js/mermaid.min.js", function () {
        console.log('script loaded.')
    });
}
function appendMarkmapScript(callback){
    addScript("/api/ai/static/js/markmap/lib/index.iife.min.js", function () {
        addScript("/api/ai/static/js/markmap/d3.min.js", function () {
            addScript("/api/ai/static/js/markmap/view/index.min.js", callback)
        })
    });
}

function transform(transformer, content) {
    const result = transformer.transform(content);
    const keys = Object.keys(result.features).filter((key) => !enabled[key]);
    keys.forEach((key) => {
        enabled[key] = true;
    });
    const { styles, scripts } = transformer.getAssets(keys);
    const { markmap } = window;
    if (styles)
        markmap.loadCSS(styles);
    if (scripts)
        markmap.loadJS(scripts);
    return result;
}
function render(el) {
    var _a2;
    const { Transformer, Markmap, deriveOptions } = window.markmap;
    const lines = ((_a2 = el.textContent) == null ? void 0 : _a2.split("\n")) || [];
    let indent = Infinity;
    lines.forEach((line) => {
        var _a3;
        const spaces = ((_a3 = line.match(/^\s*/)) == null ? void 0 : _a3[0].length) || 0;
        if (spaces < line.length)
            indent = Math.min(indent, spaces);
    });
    const content = lines.map((line) => line.slice(indent)).join("\n").trim();
    const transformer = new Transformer();
    el.innerHTML = "<svg></svg>";
    const svg = el.firstChild;
    const mm = Markmap.create(svg, { embedGlobalCSS: false });
    const doRender = () => {
        const { root, frontmatter } = transform(transformer, content);
        const markmapOptions = frontmatter == null ? {colorFreezeLevel:2} : frontmatter.markmap;
        const frontmatterOptions = deriveOptions(markmapOptions);
        mm.setData(root, frontmatterOptions);
        setTimeout(()=>{mm.fit()}, 100);
        //mm.fit();
    };
    transformer.hooks.retransform.tap(doRender);
    doRender();
}
document.addEventListener("DOMContentLoaded", function() {
    renderMathInElement(document.body, {
        delimiters: [
            {left: '$$', right: '$$', display: true},
        {left: '$', right: '$', display: false},
        {left: '\\(', right: '\\)', display: false},
        {left: '\\[', right: '\\]', display: true}
    ],
        throwOnError : false
    });
    var md = window.markdownit({html: true, langPrefix:''});
    var div = document.getElementsByClassName('markdown');
    for(var i = 0; i < div.length; i++) {
        var htmlContent = div[i].innerHTML;
        document.getElementsByClassName('markdown')[i].innerHTML = md.render(htmlContent).replaceAll('&amp;','&');
    }
    var maps = document.querySelectorAll(".markmap");
    if(maps.length > 0){
        appendMarkmapScript(function(){
            maps.forEach(render);
        })
    }
    hljs.configure({
        cssSelector:'pre > code:not(.markmap, .mermaid)'
    });
    hljs.highlightAll();
    if(document.querySelectorAll(".mermaid").length > 0){
        appendMermaidScript();
    }
});