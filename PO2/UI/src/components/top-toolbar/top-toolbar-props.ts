import type { ToolbarWorkspaceItem } from "./toolbar-types";

export type TopToolbarProps = {
    workspaceItems: ToolbarWorkspaceItem[];
    copyActive: boolean;
    pasteActive: boolean;
    copyDisabled: boolean;
    pasteDisabled: boolean;
    onCopy: () => void;
    onPaste: () => void;
    deleteActive: boolean;
    deleteDisabled: boolean;
    onDelete: () => void;
    helpActive: boolean;
    helpDisabled?: boolean;
    backDisabled?: boolean;
    closeDisabled?: boolean;
    saveManagerActive?: boolean;
    saveManagerDisabled?: boolean;
    onSaveManager?: () => void;
    onBack: () => void;
    onHelp: () => void;
    onClose: () => void;
};

