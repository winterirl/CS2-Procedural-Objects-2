import { ModRegistrar } from "cs2/modding";
import {
    ProceduralObjectsButton,
    ProceduralObjectsPanel,
} from "@/procedural-objects-panel";

const register: ModRegistrar = (moduleRegistry) => {
    moduleRegistry.append("GameTopLeft", ProceduralObjectsButton); // top
    moduleRegistry.append("Game", ProceduralObjectsPanel); // left
};

export default register;
