/** How academies reach the Ojas team: sales on the landing page, upgrades on the Subscription page. */
export const CONTACT = {
  phone: '+91 85548 23947',
  tel: 'tel:+918554823947',
  email: 'info.ojasedu@gmail.com',
  whatsappNumber: '918554823947',
};

/** A WhatsApp chat with the Ojas team, opened with the given message ready to send. */
export function whatsappLink(message: string): string {
  return `https://wa.me/${CONTACT.whatsappNumber}?text=${encodeURIComponent(message)}`;
}
