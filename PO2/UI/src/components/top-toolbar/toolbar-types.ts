export type ToolbarWorkspaceItem = {
    id: string;
    label: string;
    tooltip?: string;
    active: boolean;
    disabled?: boolean;
    disabledReason?: string;
    onSelect: () => void;
};
