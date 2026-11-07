
import { Menu, Bell, ChevronDown } from "lucide-react";

interface HeaderProps {
  onMenuClick: () => void;
}

export default function Header({
  onMenuClick
}: HeaderProps) {
  return (
    <header className="app-header">
      <div className="header-left">
        <button
          className="menu-button"
          onClick={onMenuClick}
          aria-label="Open sidebar"
        >
          <Menu size={22} />
        </button>

        <div className="header-breadcrumb">
          <span>Workspace</span>
          <span className="breadcrumb-separator">/</span>
          <strong>Dashboard</strong>
        </div>
      </div>

      <div className="header-right">
        <button
          className="notification-button"
          aria-label="Notifications"
        >
          <Bell size={19} />
          <span className="notification-dot" />
        </button>

        <div className="header-profile">
          <div className="profile-avatar">U</div>

          <div className="profile-info">
            <strong>User</strong>
            <span>Personal</span>
          </div>

          <ChevronDown size={16} />
        </div>
      </div>
    </header>
  );
}
