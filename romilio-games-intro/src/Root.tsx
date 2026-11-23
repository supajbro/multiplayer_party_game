import { RomilioGamesIntroComposition } from "./RomilioGamesIntro";
import { GTA6RecordCompositions } from "./GTA6RecordShort";
import { LeonidaCompositions } from "./Leonida/Short";

export const RemotionRoot: React.FC = () => {
  return (
    <>
      <RomilioGamesIntroComposition />
      <GTA6RecordCompositions />
      <LeonidaCompositions />
    </>
  );
};
