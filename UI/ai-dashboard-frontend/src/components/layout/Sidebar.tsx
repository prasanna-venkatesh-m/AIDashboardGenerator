
import {
  LayoutDashboard,
  MessageSquare,
  Files,
  History,
  Settings,
  Plus,
  X,
  ChartNoAxesCombined
} from "lucide-react";

import { NavLink } from "react-router-dom";

interface SidebarProps {
  isOpen: boolean;
  onClose: () => void;
}

const navigationItems = [
  {
    title: "Dashboard",
    path: "/",
    icon: LayoutDashboard
  },
  {
    title: "New Chat",
    path: "/chat",
    icon: MessageSquare
  },
  {
    title: "My Files",
    path: "/files",
    icon: Files
  },
  {
    title: "History",
    path: "/history",
    icon: History
  }
];

export default function Sidebar({
  isOpen,
  onClose
}: SidebarProps) {
  return (
    <>
      {isOpen && (
        <div
          className="sidebar-overlay"
          onClick={onClose}
        />
      )}

      <aside className={`sidebar ${isOpen ? "sidebar-open" : ""}`}>
        <div className="sidebar-header">
          <div className="brand">
            <div className="brand-icon">
              <ChartNoAxesCombined size={22} />
            </div>

            <div className="brand-info">
              <h2>InsightAI</h2>
              <span>Dashboard Generator</span>
            </div>
          </div>

          <button
            className="sidebar-close"
            onClick={onClose}
            aria-label="Close sidebar"
          >
            <X size={20} />
          </button>
        </div>

        <div className="sidebar-content">
          <NavLink
            to="/chat"
            className="new-chat-button"
            onClick={onClose}
          >
            <Plus size={19} />
            <span>New Dashboard</span>
          </NavLink>

          <div className="sidebar-section">
            <span className="sidebar-label">
              WORKSPACE
            </span>

            <nav className="sidebar-nav">
              {navigationItems.map((item) => {
                const Icon = item.icon;

                return (
                  <NavLink
                    key={item.path}
                    to={item.path}
                    end={item.path === "/"}
                    onClick={onClose}
                    className={({ isActive }) =>
                      `nav-item ${isActive ? "active" : ""}`
                    }
                  >
                    <Icon size={19} />
                    <span>{item.title}</span>
                  </NavLink>
                );
              })}
            </nav>
          </div>
        </div>

        <div className="sidebar-footer">
          <NavLink
            to="/settings"
            className={({ isActive }) =>
              `nav-item ${isActive ? "active" : ""}`
            }
            onClick={onClose}
          >
            <Settings size={19} />
            <span>Settings</span>
          </NavLink>

          <div className="sidebar-user">
            <div className="user-avatar">U</div>

            <div className="user-info">
              <strong>My Workspace</strong>
              <span>Personal Account</span>
            </div>
          </div>
        </div>
      </aside>
    </>
  );
}
