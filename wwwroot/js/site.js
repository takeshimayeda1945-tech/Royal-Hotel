// Royal Hotel – small enhancements shared by every page (both layouts, log-in and sign-up).
// Everything also works without JavaScript; these only make the pages friendlier.

// ---------------------------------------------------------------- dates
// Keep a check-out date after its check-in partner (search bar, room details and checkout forms).
// Inputs are paired inside the same <form> through data-role="check-in" / data-role="check-out";
// data-max-nights on the check-out input limits the length of the stay.
function toDateValue(date) {
    return [
        date.getFullYear(),
        String(date.getMonth() + 1).padStart(2, '0'),
        String(date.getDate()).padStart(2, '0')
    ].join('-');
}

document.addEventListener('change', (event) => {
    const checkIn = event.target;
    if (!(checkIn instanceof HTMLInputElement) || checkIn.dataset.role !== 'check-in' || !checkIn.value) {
        return;
    }

    const checkOut = checkIn.form ? checkIn.form.querySelector('input[data-role="check-out"]') : null;
    const start = new Date(`${checkIn.value}T00:00:00`);
    if (!checkOut || Number.isNaN(start.getTime())) {
        return;
    }

    const nextDay = new Date(start);
    nextDay.setDate(nextDay.getDate() + 1);
    const minCheckOut = toDateValue(nextDay);
    checkOut.min = minCheckOut;

    const maxNights = Number.parseInt(checkOut.dataset.maxNights ?? '', 10);
    let maxCheckOut = '';
    if (maxNights > 0) {
        const lastDay = new Date(start);
        lastDay.setDate(lastDay.getDate() + maxNights);
        maxCheckOut = toDateValue(lastDay);
        checkOut.max = maxCheckOut;
    }

    if (!checkOut.value || checkOut.value < minCheckOut || (maxCheckOut && checkOut.value > maxCheckOut)) {
        checkOut.value = minCheckOut;
        checkOut.dispatchEvent(new Event('change', { bubbles: true }));
    }
});

// ---------------------------------------------------------------- forms
// <form data-confirm="…"> asks before it is sent (cancel, delete, refund …).
// A form that is already being sent ignores more clicks, so a double click on "confirm booking"
// or "pay" can never send it twice.
document.addEventListener('submit', (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement)) {
        return;
    }

    if (form.dataset.submitting) {
        event.preventDefault();
        return;
    }

    if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
        event.preventDefault();
        return;
    }

    // GET forms (search) may be sent again at once; only POST forms are locked.
    if ((form.getAttribute('method') || 'get').toLowerCase() === 'post') {
        form.dataset.submitting = 'true';
        form.setAttribute('aria-busy', 'true');
    }
});

// Coming back with the browser's Back button shows the page from memory: unlock its forms.
window.addEventListener('pageshow', () => {
    document.querySelectorAll('form[data-submitting]').forEach((form) => {
        delete form.dataset.submitting;
        form.removeAttribute('aria-busy');
    });
});

// ---------------------------------------------------------------- small buttons
document.addEventListener('click', (event) => {
    const target = event.target instanceof Element ? event.target.closest('[data-print], [data-toggle-password], [data-fill-login]') : null;
    if (!target) {
        return;
    }

    // <button data-print> prints the page (booking confirmation).
    if (target.hasAttribute('data-print')) {
        window.print();
        return;
    }

    // <button data-toggle-password="#Password"> shows / hides the password.
    if (target.hasAttribute('data-toggle-password')) {
        const input = document.querySelector(target.getAttribute('data-toggle-password'));
        if (input instanceof HTMLInputElement) {
            const show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            target.textContent = show ? 'ซ่อน' : 'แสดง';
            target.setAttribute('aria-pressed', String(show));
            input.focus();
        }
        return;
    }

    // Demo accounts on the log-in page (Development only): fill in the form.
    if (target.hasAttribute('data-fill-login')) {
        const email = document.getElementById('Email');
        const password = document.getElementById('Password');
        if (email instanceof HTMLInputElement && password instanceof HTMLInputElement) {
            email.value = target.getAttribute('data-email') ?? '';
            password.value = target.getAttribute('data-password') ?? '';
            password.focus();
        }
    }
});
