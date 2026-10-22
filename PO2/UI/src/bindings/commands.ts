import { trigger } from "cs2/api";

export const group = "po2";
export const run = (name: string) => () => trigger(group, name);
