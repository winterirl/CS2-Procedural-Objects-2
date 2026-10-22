import { useValue } from "cs2/api";
import { useEffect, useRef, useState } from "react";
import { deleteCursorVisible$ } from "@/bindings/navigation";
import bulldozerIcon from "@/images/navigation/bulldozer.svg";
import styles from "./delete-cursor.module.scss";

export const DeleteCursor = () => {
    const visible = useValue(deleteCursorVisible$);
    const cursor = useRef<HTMLDivElement>(null);
    const [positioned, setPositioned] = useState(false);

    useEffect(() => {
        let hasPosition = false;
        const move = (event: MouseEvent) => {
            if (!cursor.current) return;
            // DOM coordinates already include the game's UI scaling.
            cursor.current.style.left = `${event.clientX}px`;
            cursor.current.style.top = `${event.clientY}px`;
            if (!hasPosition) {
                hasPosition = true;
                setPositioned(true);
            }
        };
        const hide = () => {
            hasPosition = false;
            setPositioned(false);
        };
        document.addEventListener("mousemove", move);
        document.addEventListener("mouseleave", hide);
        window.addEventListener("blur", hide);
        return () => {
            document.removeEventListener("mousemove", move);
            document.removeEventListener("mouseleave", hide);
            window.removeEventListener("blur", hide);
        };
    }, []);

    return <div ref={cursor} className={styles.cursor} aria-hidden="true"
        style={{ visibility: visible && positioned ? "visible" : "hidden" }}>
        <img src={bulldozerIcon} alt="" draggable={false} />
    </div>;
};
