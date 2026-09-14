import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import en from "./locales/en";
import ru from "./locales/ru";

export const languages = ["en", "ru"] as const;
export type Language = (typeof languages)[number];

const resources = {
  en: { translation: en },
  ru: { translation: ru }
} as const;

function getInitialLanguage(): Language {
  const saved = localStorage.getItem("alegacy-language");
  if (saved === "en" || saved === "ru") return saved;
  return "ru";
}

void i18n
  .use(initReactI18next)
  .init({
    resources,
    lng: getInitialLanguage(),
    fallbackLng: "en",
    supportedLngs: languages,
    interpolation: { escapeValue: false }
  });

i18n.on("languageChanged", (language) => {
  if (languages.includes(language as Language)) {
    localStorage.setItem("alegacy-language", language);
  }
});

export default i18n;
