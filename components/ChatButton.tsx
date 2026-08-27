import SchoolOutlinedIcon from "@mui/icons-material/SchoolOutlined";
import { Fab, Tooltip } from "@mui/material";

type Props = {
  onClick: () => void;
};
export const ChatButton = ({
  onClick,
}: Props) => {
  return (
    <Tooltip title="The Learning Matchmaker" placement="left">
      <Fab
        onClick={onClick}
        aria-label="The Learning Matchmaker öffnen"
        sx={{
          position: "fixed",
          right: 22,
          bottom: 24,
          zIndex: 9999,
          width: 56,
          height: 56,
          color: "#fff",
          background: "linear-gradient(145deg, #0078D6 0%, #033052 100%)",
          boxShadow: "0 10px 28px rgba(31, 79, 90, 0.35)",
          transition: "transform 0.2s ease, box-shadow 0.2s ease",
          "&:hover": {
            background: "linear-gradient(145deg, #033052 0%, #0078D6 100%)",
            boxShadow: "0 12px 32px #5ab4f8bd)",
            transform: "translateY(-2px)",
          },
        }}
      >
        <SchoolOutlinedIcon />
      </Fab>
    </Tooltip>
  );
};
