import SmartToyIcon from '@mui/icons-material/SmartToy';
import { Fab } from '@mui/material';

type Props = {
  onClick: () => void;
};

export const ChatButton = ({ onClick }: Props) => {
  return (
    <Fab
      color="primary"
      onClick={onClick}
      aria-label="The Learning Matchmaker öffnen"
      sx={{
        position: 'fixed',
        right: 24,
        bottom: 24,
        zIndex: 9999
      }}
    >
      <SmartToyIcon />
    </Fab>
  );
};
