// Monaco Editor JS Interop for LeanKernel Blazor
// Manages editor instances keyed by element ID

const monacoEditors = {};

export function initMonaco(elementId, language, value, readOnly, dotNetRef) {
    const container = document.getElementById(elementId);
    if (!container) return;

    // Destroy existing instance if any
    if (monacoEditors[elementId]) {
        monacoEditors[elementId].dispose();
        delete monacoEditors[elementId];
    }

    require.config({ paths: { vs: 'https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.52.0/min/vs' } });
    require(['vs/editor/editor.main'], () => {
        const editor = monaco.editor.create(container, {
            value: value || '',
            language: language || 'plaintext',
            theme: 'vs-dark',
            readOnly: readOnly || false,
            automaticLayout: true,
            minimap: { enabled: false },
            fontSize: 13,
            fontFamily: "'JetBrains Mono', 'Cascadia Code', 'Fira Code', monospace",
            lineNumbers: 'on',
            scrollBeyondLastLine: false,
            renderWhitespace: 'none',
            wordWrap: language === 'markdown' || language === 'plaintext' ? 'on' : 'off',
            padding: { top: 12, bottom: 12 },
            scrollbar: { verticalScrollbarSize: 6, horizontalScrollbarSize: 6 },
            overviewRulerLanes: 0,
        });

        monacoEditors[elementId] = editor;

        editor.onDidChangeModelContent(() => {
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnValueChanged', editor.getValue());
            }
        });
    });
}

export function getValue(elementId) {
    const editor = monacoEditors[elementId];
    return editor ? editor.getValue() : '';
}

export function setValue(elementId, value) {
    const editor = monacoEditors[elementId];
    if (editor) {
        const model = editor.getModel();
        model.pushEditOperations([], [{
            range: model.getFullModelRange(),
            text: value
        }], () => null);
    }
}

export function setLanguage(elementId, language) {
    const editor = monacoEditors[elementId];
    if (editor) {
        monaco.editor.setModelLanguage(editor.getModel(), language);
    }
}

export function setReadOnly(elementId, readOnly) {
    const editor = monacoEditors[elementId];
    if (editor) {
        editor.updateOptions({ readOnly });
    }
}

export function disposeMonaco(elementId) {
    if (monacoEditors[elementId]) {
        monacoEditors[elementId].dispose();
        delete monacoEditors[elementId];
    }
}

export function layout(elementId) {
    const editor = monacoEditors[elementId];
    if (editor) editor.layout();
}
