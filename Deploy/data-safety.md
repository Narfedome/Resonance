# Google Play : section « Sécurité des données »

À remplir dans la Play Console › **Règles et programmes › Contenu de l'application › Sécurité des données**, à partir de la
version qui contient « Nous écrire » (formulaire de signalement sans compte, relais `relay/` du dépôt Support). Avant ça,
l'appli ne transmettait rien et la réponse était « aucune donnée collectée ».

Ce qui part, et seulement quand l'utilisateur appuie sur « Envoyer » : le message (type, résumé, détail, « Où ? »), les
captures d'écran qu'il ajoute, et, si la case est cochée, les informations techniques (version de l'appli, modèle et
système de l'appareil, langue, dernières lignes du journal d'erreurs). Aucun identifiant, aucun compte, aucune adresse
e-mail. Transport en HTTPS vers un Worker Cloudflare, stockage dans un dépôt GitHub privé, tri par une IA (Anthropic).

## Vue d'ensemble

| Question | Réponse |
|---|---|
| Votre appli collecte-t-elle ou partage-t-elle des données utilisateur des types requis ? | **Oui** |
| Toutes les données collectées sont-elles chiffrées en transit ? | **Oui** (HTTPS) |
| Proposez-vous un moyen de demander la suppression des données ? | **Oui**. URL de suppression des données : `https://resonance-app.com/data-deletion.html` (procédure par e-mail, données supprimées et durées de conservation) |

## Types de données

Pour chacun : **Collecté** oui, **Partagé** non, **Traité de manière éphémère** non, **Obligatoire ou facultatif**
facultatif (l'utilisateur choisit d'envoyer), **Finalité** fonctionnalité de l'appli.

| Catégorie | Type | Pourquoi |
|---|---|---|
| Activité dans l'appli | Autres contenus générés par l'utilisateur | Le texte du message |
| Photos et vidéos | Photos | Captures d'écran jointes |
| Infos et performances de l'appli | Journaux de plantage | Dernières lignes du journal d'erreurs |
| Infos et performances de l'appli | Diagnostics | Version de l'appli, modèle et système de l'appareil, langue |

Rien d'autre à cocher : ni position, ni infos personnelles (nom, e-mail), ni identifiants d'appareil, ni contacts.

**« Partagé : non »** : Cloudflare, GitHub et Anthropic traitent les données pour le compte du développeur (prestataires),
ce que Google ne compte pas comme un partage avec des tiers.

## Politique de confidentialité

Adresse à renseigner : `https://resonance-app.com/privacy.html` (déjà à jour, section « Messages envoyés depuis
« Nous écrire » »).

## Microsoft Store

Pas d'équivalent détaillé : Partner Center › Properties › cocher « This product accesses, collects or transmits personal
information » et donner la même adresse de politique de confidentialité.
