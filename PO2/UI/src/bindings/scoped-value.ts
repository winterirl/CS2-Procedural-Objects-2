import { bindLocalValue, bindValue, useValue, type ValueBinding } from "cs2/api";

// Create these once at module scope, just like regular bindings.
// A hidden panel subscribes to its local default instead of polling C#.
export const bindScopedValue = <T>(group: string, name: string, fallback: T) => ({
    live: bindValue<T>(group, name, fallback),
    hidden: bindLocalValue<T>(fallback),
});

export const useScopedValue = <T>(
    binding: { live: ValueBinding<T>; hidden: ValueBinding<T> },
    visible: boolean,
): T => useValue(visible ? binding.live : binding.hidden);
