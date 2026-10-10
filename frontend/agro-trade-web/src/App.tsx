import { BrowserRouter, Route, Routes } from 'react-router-dom'
import LandingPage from './pages/LandingPage'
import AboutPage from './pages/AboutPage'
import ContactPage from './pages/ContactPage'
import LoginPage from './pages/LoginPage'
import RegisterPage from './pages/RegisterPage'
import ForgotPasswordPage from './pages/ForgotPasswordPage'
import ResetPasswordPage from './pages/ResetPasswordPage'
import ConfirmEmailPage from './pages/ConfirmEmailPage'
import DashboardPage from './pages/DashboardPage'
import MarketplacePage from './pages/MarketplacePage'
import MarketIntelligencePage from './pages/MarketIntelligencePage'
import ProductDetailPage from './pages/ProductDetailPage'
import ProductFormPage from './pages/ProductFormPage'
import MyProductsPage from './pages/MyProductsPage'
import RfqDirectoryPage from './pages/RfqDirectoryPage'
import RfqPublishPage from './pages/RfqPublishPage'
import RfqDetailPage from './pages/RfqDetailPage'
import MyRfqsPage from './pages/MyRfqsPage'
import OrdersListPage from './pages/OrdersListPage'
import OrderDetailPage from './pages/OrderDetailPage'
import MessagesPage from './pages/MessagesPage'
import NotificationsPage from './pages/NotificationsPage'
import BusinessHubPage from './pages/BusinessHubPage'
import BusinessOnboardingPage from './pages/BusinessOnboardingPage'
import BusinessDetailPage from './pages/BusinessDetailPage'
import PublicBusinessPage from './pages/PublicBusinessPage'
import TrackShipmentPage from './pages/TrackShipmentPage'
import AdminDashboardPage from './pages/AdminDashboardPage'
import AdminVerificationPage from './pages/AdminVerificationPage'
import AdminDocumentsPage from './pages/AdminDocumentsPage'
import AdminOrdersPage from './pages/AdminOrdersPage'
import AdminShipmentsPage from './pages/AdminShipmentsPage'
import AdminUsersPage from './pages/AdminUsersPage'
import AdminAuditLogPage from './pages/AdminAuditLogPage'
import { ProtectedRoute } from './components/ProtectedRoute'
import { RequireRole } from './components/RequireRole'

const ADMIN_ROLES = ['Administrator']

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<LandingPage />} />
        <Route path="/about" element={<AboutPage />} />
        <Route path="/contact" element={<ContactPage />} />
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        <Route path="/confirm-email" element={<ConfirmEmailPage />} />

        {/* Marketplace (public) */}
        <Route path="/marketplace" element={<MarketplacePage />} />
        <Route path="/market-intelligence" element={<MarketIntelligencePage />} />
        <Route path="/products/:idOrSlug" element={<ProductDetailPage />} />
        <Route path="/businesses/:id" element={<PublicBusinessPage />} />
        <Route path="/rfqs" element={<RfqDirectoryPage />} />

        {/*
          Guest consignment tracking. Deliberately outside ProtectedRoute: the link goes to freight
          forwarders and customs agents who have no account here and will not make one. The server
          returns a narrowed payload with no order, party, goods or money in it, and answers 404
          for any token it does not recognise.
        */}
        <Route path="/track/:token" element={<TrackShipmentPage />} />

        {/* Authenticated */}
        <Route
          path="/dashboard"
          element={
            <ProtectedRoute>
              <DashboardPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/products/new"
          element={
            <ProtectedRoute>
              <ProductFormPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/products/:id/edit"
          element={
            <ProtectedRoute>
              <ProductFormPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/my/products"
          element={
            <ProtectedRoute>
              <MyProductsPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/rfqs/new"
          element={
            <ProtectedRoute>
              <RfqPublishPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/rfqs/:id"
          element={
            <ProtectedRoute>
              <RfqDetailPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/my/rfqs"
          element={
            <ProtectedRoute>
              <MyRfqsPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/dashboard/orders"
          element={
            <ProtectedRoute>
              <OrdersListPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/dashboard/orders/:id"
          element={
            <ProtectedRoute>
              <OrderDetailPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/messages"
          element={
            <ProtectedRoute>
              <MessagesPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/notifications"
          element={
            <ProtectedRoute>
              <NotificationsPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/business"
          element={
            <ProtectedRoute>
              <BusinessHubPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/business/new"
          element={
            <ProtectedRoute>
              <BusinessOnboardingPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/business/:id"
          element={
            <ProtectedRoute>
              <BusinessDetailPage />
            </ProtectedRoute>
          }
        />

        {/* Admin */}
        <Route
          path="/admin"
          element={
            <RequireRole roles={ADMIN_ROLES}>
              <AdminDashboardPage />
            </RequireRole>
          }
        />
        <Route
          path="/admin/verification"
          element={
            <RequireRole roles={ADMIN_ROLES}>
              <AdminVerificationPage />
            </RequireRole>
          }
        />
        <Route
          path="/admin/documents"
          element={
            <RequireRole roles={ADMIN_ROLES}>
              <AdminDocumentsPage />
            </RequireRole>
          }
        />
        <Route
          path="/admin/orders"
          element={
            <RequireRole roles={ADMIN_ROLES}>
              <AdminOrdersPage />
            </RequireRole>
          }
        />
        <Route
          path="/admin/shipments"
          element={
            <RequireRole roles={ADMIN_ROLES}>
              <AdminShipmentsPage />
            </RequireRole>
          }
        />
        <Route
          path="/admin/users"
          element={
            <RequireRole roles={ADMIN_ROLES}>
              <AdminUsersPage />
            </RequireRole>
          }
        />
        <Route
          path="/admin/audit-log"
          element={
            <RequireRole roles={ADMIN_ROLES}>
              <AdminAuditLogPage />
            </RequireRole>
          }
        />

        <Route path="*" element={<LandingPage />} />
      </Routes>
    </BrowserRouter>
  )
}