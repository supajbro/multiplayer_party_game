import { useCurrentFrame, interpolate } from "remotion";
import { Stage, Panel, Label, Head, pink, orange, clamp } from "./Design";
export const Hook = () => {
  const f = useCurrentFrame();
  return (
    <Stage
      chapter="01 / THE IMPOSSIBLE"
      note="Original procedural artwork • No gameplay footage"
    >
      <Panel top={300}>
        <Label>One game. One day.</Label>
        <div style={{ marginTop: 35 }}>
          <Head size={130}>
            MORE THAN
            <br />
            <span style={{ color: orange }}>AVATAR?!</span>
          </Head>
        </div>
      </Panel>
      <div
        style={{
          position: "absolute",
          left: 130,
          top: 740,
          width: 760,
          height: 480,
          rotate: `${-7 + Math.sin(f / 35) * 2}deg`,
          scale: 1 + f * 0.0008,
          border: "3px solid #ffb36baa",
          background: "linear-gradient(150deg,#311543,#080b1e)",
          boxShadow: `0 0 100px ${pink}55`,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
        }}
      >
        <div
          style={{
            fontFamily: "Impact",
            fontSize: 280,
            color: pink,
            textShadow: "8px 8px #ffb36b",
          }}
        >
          VI
        </div>
        <div
          style={{
            position: "absolute",
            bottom: 30,
            fontSize: 30,
            letterSpacing: 10,
          }}
        >
          VICE CITY / 2026
        </div>
      </div>
      <div
        style={{
          position: "absolute",
          top: 1210,
          left: 80,
          right: 100,
          background: "#101121e8",
          padding: "32px 40px",
          borderTop: `4px solid ${orange}`,
          rotate: "3deg",
          opacity: interpolate(f, [25, 40], [0, 1], clamp),
        }}
      >
        <Label>Worldwide cinema lifetime</Label>
        <Head size={85}>
          AVATAR <span style={{ color: orange }}>$2.92B</span>
        </Head>
        <div style={{ fontSize: 28, marginTop: 14, color: "#c5bdd4" }}>
          ▰ ▰ ▰ ▰ ▰ ▰ ▰ ▰ ▰ ▰ ▰ ▰
        </div>
      </div>
    </Stage>
  );
};
