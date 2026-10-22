const fs = require("node:fs");
const path = require("node:path");
const ts = require("typescript");
const directory = path.resolve(__dirname, "../../Localization");
const englishPath = path.join(directory, "en-US.json");
const english = JSON.parse(fs.readFileSync(englishPath, "utf8"));
const tokens = (value) =>
    [
        ...new Set(
            Array.from(
                value.matchAll(/\{([A-Za-z][A-Za-z0-9_]*)\}/g),
                (match) => match[1],
            ),
        ),
    ]
        .sort()
        .join(",");
const validText = (value) =>
    typeof value === "string" &&
    value.trim().length > 0 &&
    !/[{}]/.test(value.replace(/\{[A-Za-z][A-Za-z0-9_]*\}/g, ""));
const errors = [],
    warnings = [];
for (const [key, value] of Object.entries(english)) {
    if (!/^PO2(?:\.[A-Za-z][A-Za-z0-9]*)+$/.test(key) || !validText(value))
        errors.push(`Invalid English entry: ${key}`);
}
for (const name of fs
    .readdirSync(directory)
    .filter((name) => name.endsWith(".json"))) {
    const text = fs.readFileSync(path.join(directory, name), "utf8");
    const issues = name === "en-US.json" ? errors : warnings;
    let entries;
    try {
        entries = JSON.parse(text);
    } catch (error) {
        issues.push(`${name}: ${error.message}`);
        continue;
    }
    if (!entries || Array.isArray(entries) || typeof entries !== "object") {
        issues.push(`${name}: expected an object of text entries`);
        continue;
    }
    const ast = ts.parseJsonText(name, text);
    const properties = ast.statements[0]?.expression?.properties || [];
    const seen = new Set();
    for (const property of properties) {
        const key = property.name?.text;
        if (seen.has(key)) issues.push(`${name}: duplicate key ${key}`);
        seen.add(key);
    }
    if (name === "en-US.json") continue;
    for (const [key, value] of Object.entries(entries)) {
        if (!(key in english)) warnings.push(`${name}: unknown key ${key}`);
        else if (typeof value === "string" && !value.trim()) continue;
        else if (!validText(value) || tokens(value) !== tokens(english[key]))
            warnings.push(`${name}: invalid value or placeholders for ${key}`);
    }
}

const sourceRoot = path.resolve(__dirname, "../src");
const settingsAliases = fs.readFileSync(
    path.join(directory, "SettingsLocales.cs"),
    "utf8",
);

for (const match of settingsAliases.matchAll(/"(PO2\.Settings\.[^"]+)"/g)) {
    if (!(match[1] in english))
        errors.push(`Settings alias has no English entry: ${match[1]}`);
}

const hotkeyCatalog = fs.readFileSync(
    path.join(directory, "../Input/HotkeyCatalog.cs"),
    "utf8",
);

for (const match of hotkeyCatalog.matchAll(
    /new HotkeyDefinition\("([^"]+)", "([^"]+)", [^\n]+/g,
)) {
    if (match[0].includes(", false)")) continue;
    for (const key of [
        `PO2.Settings.Hotkeys.Action.${match[1]}`,
        `PO2.Settings.Hotkeys.Description.${match[1]}`,
        `PO2.Settings.Hotkeys.Group.${match[2]}`,
    ]) {
        if (!(key in english))
            errors.push(`Hotkey has no English entry: ${key}`);
    }
}

function sourceFiles(directory, prefix = "") {
    return fs
        .readdirSync(directory, { withFileTypes: true })
        .flatMap((entry) => {
            if (
                ["node_modules", "bin", "obj", "Tests", ".git"].includes(
                    entry.name,
                )
            )
                return [];
            const relative = path.join(prefix, entry.name);
            return entry.isDirectory()
                ? sourceFiles(path.join(directory, entry.name), relative)
                : [relative];
        });
}

function callArguments(text, start) {
    const args = [];
    let depth = 0,
        begin = start;
    for (let i = start; i < text.length; i++) {
        const char = text[i];
        if (char === '"' || char === "'") {
            const quote = char;
            for (i++; i < text.length; i++) {
                if (text[i] === "\\") i++;
                else if (text[i] === quote) break;
            }
        } else if ("([{".includes(char)) depth++;
        else if (")]}".includes(char)) {
            if (depth === 0) {
                args.push(text.slice(begin, i).trim());
                return args;
            }
            depth--;
        } else if (char === "," && depth === 0) {
            args.push(text.slice(begin, i).trim());
            begin = i + 1;
        }
    }
    return [];
}

const projectRoot = path.resolve(directory, "..");

for (const relative of sourceFiles(projectRoot).filter((name) =>
    name.endsWith(".cs"),
)) {
    const text = fs.readFileSync(path.join(projectRoot, relative), "utf8");
    for (const match of text.matchAll(/LocalizedText\.For\s*\(/g)) {
        const args = callArguments(text, match.index + match[0].length);
        const key = /^"(PO2\.[^"]+)"$/.exec(args[0])?.[1];
        if (!key || !(key in english)) {
            errors.push(`${relative}: unknown runtime locale key ${args[0]}`);
            continue;
        }
        const names = args
            .slice(1)
            .map((arg) => /^\(\s*"([^"]+)"\s*,/.exec(arg)?.[1]);
        if (
            names.some((name) => !name) ||
            new Set(names).size !== names.length ||
            names.sort().join(",") !== tokens(english[key])
        )
            errors.push(`${relative}: runtime arguments do not match ${key}`);
    }
}

for (const relative of sourceFiles(sourceRoot).filter((name) =>
    /\.tsx?$/.test(name),
)) {
    const source = ts.createSourceFile(
        relative,
        fs.readFileSync(path.join(sourceRoot, relative), "utf8"),
        ts.ScriptTarget.Latest,
        true,
    );
    const checkArguments = (key, args) => {
        if (!(key in english)) return;
        const provided =
            args && ts.isObjectLiteralExpression(args)
                ? args.properties
                      .map((property) => property.name?.getText(source))
                      .sort()
                      .join(",")
                : "";
        if (tokens(english[key]) !== provided)
            errors.push(`${relative}: arguments do not match ${key}`);
    };
    const visit = (node) => {
        if (
            ts.isStringLiteral(node) &&
            node.text.startsWith("PO2.") &&
            !(node.text in english)
        )
            errors.push(`${relative}: unknown locale key ${node.text}`);
        if (
            ts.isCallExpression(node) &&
            node.expression.getText(source) === "t" &&
            node.arguments[0] &&
            ts.isStringLiteral(node.arguments[0])
        )
            checkArguments(node.arguments[0].text, node.arguments[1]);
        if (
            ts.isJsxSelfClosingElement(node) &&
            node.tagName.getText(source) === "LocalizedText"
        ) {
            const attributes = node.attributes.properties;
            const id = attributes.find(
                (attribute) => attribute.name?.getText(source) === "id",
            )?.initializer;
            const args = attributes.find(
                (attribute) => attribute.name?.getText(source) === "args",
            )?.initializer;
            if (id && ts.isStringLiteral(id))
                checkArguments(id.text, args?.expression);
        }
        ts.forEachChild(node, visit);
    };
    visit(source);
}

for (const warning of warnings)
    console.warn(
        `[PO2 localization] ${warning}; runtime will use English where needed.`,
    );
    
if (errors.length) {
    errors.forEach((error) => console.error(`[PO2 localization] ${error}`));
    process.exitCode = 1;
} else
    console.log(
        `PASS: ${Object.keys(english).length} English locale entries, UI references, and C# runtime message arguments.`,
    );
module.exports = { tokens, validText };
