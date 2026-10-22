import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";
import type { HelpTopic } from "./help-content";

type Owner = "objectName" | "partName" | "toolbar" | "texturing" | "materials" | "authoring" | "preview";
type Slots = Partial<Record<Owner, readonly HelpTopic[]>>;
type Publish = (owner: Owner, topics: readonly HelpTopic[]) => void;
const PublishContext = createContext<Publish>(() => {});
const TopicsContext = createContext<Slots>({});

// Local panel flags stay with their controls. Only discrete help topics are shared.
export const HelpContextProvider = ({ children }: { children: ReactNode }) => {
    const [slots, setSlots] = useState<Slots>({});
    const publish = useCallback<Publish>((owner, topics) => {
        setSlots(previous => {
            if ((previous[owner] ?? []).join("|") === topics.join("|")) return previous;
            return { ...previous, [owner]: topics };
        });
    }, []);
    return (
        <PublishContext.Provider value={publish}>
            <TopicsContext.Provider value={slots}>{children}</TopicsContext.Provider>
        </PublishContext.Provider>
    );
};

export const useReportHelpTopics = (owner: Owner, ...topics: (HelpTopic | null)[]) => {
    const publish = useContext(PublishContext);
    const key = topics.filter((topic): topic is HelpTopic => topic !== null).join("|");
    useEffect(() => {
        publish(owner, key ? key.split("|") as HelpTopic[] : []);
        return () => publish(owner, []);
    }, [owner, key, publish]);
};

export const useLocalHelpTopics = (): HelpTopic[] => {
    const slots = useContext(TopicsContext);
    const order: Owner[] = ["objectName", "partName", "toolbar", "texturing", "materials", "authoring", "preview"];
    return order.flatMap(owner => slots[owner] ?? []);
};
